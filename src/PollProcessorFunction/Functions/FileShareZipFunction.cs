using System.IO.Compression;
using System.Net;
using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using PollProcessorFunction.Configuration;

namespace PollProcessorFunction.Functions;

public sealed class FileShareZipFunction
{
    private const int UploadChunkSize = 4 * 1024 * 1024;

    private readonly IEnvironmentResources _resources;

    public FileShareZipFunction(IEnvironmentResources resources)
    {
        _resources = resources;
    }

    [Function("ZipFileShareFiles")]
    [OpenApiOperation(
        operationId: "ZipFileShareFiles",
        tags: new[] { "File Share" },
        Summary = "Zip multiple files from Azure File Share",
        Description = "Creates one zip file from multiple files in a source Azure File Share path and uploads it to a destination path in the same file share.")]
    [OpenApiSecurity(
        "function_key",
        SecuritySchemeType.ApiKey,
        Name = "code",
        In = OpenApiSecurityLocationType.Query)]
    [OpenApiRequestBody(
        contentType: "application/json",
        bodyType: typeof(FileShareZipRequest),
        Required = true,
        Description = "Files and Azure File Share paths used to create the zip file.")]
    [OpenApiResponseWithBody(
        statusCode: HttpStatusCode.OK,
        contentType: "application/json",
        bodyType: typeof(FileShareZipResponse),
        Summary = "Zip file created",
        Description = "Returns the destination zip file name, source/destination paths, zipped files, and size.")]
    [OpenApiResponseWithBody(
        statusCode: HttpStatusCode.BadRequest,
        contentType: "application/json",
        bodyType: typeof(FileShareZipErrorResponse),
        Summary = "Invalid request",
        Description = "Returned when required input values are missing or invalid.")]
    [OpenApiResponseWithBody(
        statusCode: HttpStatusCode.NotFound,
        contentType: "application/json",
        bodyType: typeof(FileShareZipErrorResponse),
        Summary = "Source path not found",
        Description = "Returned when the source path does not exist on the configured Azure File Share.")]
    public async Task<HttpResponseData> ZipFilesAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "fileshare/zip")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        FileShareZipRequest request;
        try
        {
            request = await ReadRequestAsync(req);
        }
        catch (InvalidOperationException ex)
        {
            return await WriteJsonAsync(
                req,
                HttpStatusCode.BadRequest,
                new FileShareZipErrorResponse { Error = ex.Message },
                cancellationToken);
        }

        var share = _resources.CreateFileShareClient();
        await share.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var sourceDirectory = GetDirectoryClient(share, request.SourcePath);
        if (!await sourceDirectory.ExistsAsync(cancellationToken))
        {
            return await WriteJsonAsync(
                req,
                HttpStatusCode.NotFound,
                new FileShareZipErrorResponse { Error = $"Source path '{request.SourcePath}' was not found." },
                cancellationToken);
        }

        var destinationDirectory = await CreateDirectoryPathAsync(
            share,
            request.DestinationPath,
            cancellationToken);

        var normalizedZipFileName = NormalizeZipFileName(request.ZipFileName);
        var destinationFile = destinationDirectory.GetFileClient(normalizedZipFileName);

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        try
        {
            var zippedFiles = await CreateZipFileAsync(
                sourceDirectory,
                request.FileNames,
                tempZipPath,
                cancellationToken);

            await UploadFileAsync(destinationFile, tempZipPath, cancellationToken);

            var properties = await destinationFile.GetPropertiesAsync(cancellationToken: cancellationToken);
            return await WriteJsonAsync(
                req,
                HttpStatusCode.OK,
                new FileShareZipResponse
                {
                    Status = "Created",
                    ZipFileName = normalizedZipFileName,
                    SourcePath = request.SourcePath,
                    DestinationPath = request.DestinationPath,
                    ZippedFiles = zippedFiles,
                    SizeInBytes = properties.Value.ContentLength
                },
                cancellationToken);
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }
    }

    private static async Task<FileShareZipRequest> ReadRequestAsync(HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        var request = string.IsNullOrWhiteSpace(body)
            ? null
            : JsonConvert.DeserializeObject<FileShareZipRequest>(body);

        if (request is null)
        {
            throw new InvalidOperationException("Request body is required.");
        }

        request.FileNames = request.FileNames
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => fileName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (request.FileNames.Count == 0)
        {
            throw new InvalidOperationException("At least one file name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ZipFileName))
        {
            throw new InvalidOperationException("ZipFileName is required.");
        }

        request.SourcePath = NormalizeDirectoryPath(request.SourcePath);
        request.DestinationPath = NormalizeDirectoryPath(request.DestinationPath);
        return request;
    }

    private static async Task<IReadOnlyList<string>> CreateZipFileAsync(
        ShareDirectoryClient sourceDirectory,
        IReadOnlyList<string> fileNames,
        string tempZipPath,
        CancellationToken cancellationToken)
    {
        var zippedFiles = new List<string>();

        await using var zipStream = File.Create(tempZipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceFile = sourceDirectory.GetFileClient(fileName);
            if (!await sourceFile.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException($"File '{fileName}' was not found in source path.");
            }

            var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var sourceStream = await sourceFile.OpenReadAsync(cancellationToken: cancellationToken);
            await sourceStream.CopyToAsync(entryStream, cancellationToken);
            zippedFiles.Add(fileName);
        }

        return zippedFiles;
    }

    private static async Task UploadFileAsync(
        ShareFileClient destinationFile,
        string tempZipPath,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(tempZipPath);
        await destinationFile.CreateAsync(fileInfo.Length, cancellationToken: cancellationToken);

        await using var stream = File.OpenRead(tempZipPath);
        var offset = 0L;
        var buffer = new byte[UploadChunkSize];

        while (offset < fileInfo.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await using var chunk = new MemoryStream(buffer, 0, bytesRead, writable: false);
            await destinationFile.UploadRangeAsync(
                new HttpRange(offset, bytesRead),
                chunk,
                cancellationToken: cancellationToken);

            offset += bytesRead;
        }
    }

    private static ShareDirectoryClient GetDirectoryClient(ShareClient share, string directoryPath)
    {
        return string.IsNullOrWhiteSpace(directoryPath)
            ? share.GetRootDirectoryClient()
            : share.GetDirectoryClient(directoryPath);
    }

    private static async Task<ShareDirectoryClient> CreateDirectoryPathAsync(
        ShareClient share,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var directory = share.GetRootDirectoryClient();
        foreach (var segment in SplitPath(directoryPath))
        {
            directory = directory.GetSubdirectoryClient(segment);
            await directory.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        return directory;
    }

    private static string NormalizeDirectoryPath(string? path)
    {
        return string.Join("/", SplitPath(path));
    }

    private static IEnumerable<string> SplitPath(string? path)
    {
        return (path ?? "")
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeZipFileName(string zipFileName)
    {
        var fileName = Path.GetFileName(zipFileName.Trim());
        return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.zip";
    }

    private static async Task<HttpResponseData> WriteJsonAsync(
        HttpRequestData req,
        HttpStatusCode statusCode,
        object value,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonConvert.SerializeObject(value, Formatting.Indented), cancellationToken);
        return response;
    }
}

public sealed class FileShareZipRequest
{
    public List<string> FileNames { get; set; } = new();

    public string SourcePath { get; set; } = "";

    public string DestinationPath { get; set; } = "";

    public string ZipFileName { get; set; } = "";
}

public sealed class FileShareZipResponse
{
    public string Status { get; set; } = "";

    public string ZipFileName { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public string DestinationPath { get; set; } = "";

    public IReadOnlyList<string> ZippedFiles { get; set; } = Array.Empty<string>();

    public long SizeInBytes { get; set; }
}

public sealed class FileShareZipErrorResponse
{
    public string Error { get; set; } = "";
}
