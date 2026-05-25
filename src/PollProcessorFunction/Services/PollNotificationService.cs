using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PollProcessorFunction.Models;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace PollProcessorFunction.Services;

public sealed class PollNotificationService : IPollNotificationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PollNotificationService> _log;

    public PollNotificationService(IConfiguration configuration, ILogger<PollNotificationService> log)
    {
        _configuration = configuration;
        _log = log;
    }

    public async Task SendUnprocessedItemsEmailAsync(
        string appId,
        IReadOnlyList<PollItem> items,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["SendGridApiKey"];
        var from = _configuration["PollNotifyEmailFrom"];
        var to = _configuration["PollNotifyEmailTo"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            _log.LogWarning(
                "Skipping unprocessed-items email for AppId {AppId}: SendGridApiKey, PollNotifyEmailFrom, or PollNotifyEmailTo not configured.",
                appId);
            return;
        }

        var client = new SendGridClient(apiKey);
        var message = new SendGridMessage
        {
            From = new EmailAddress(from),
            Subject = $"Poll Proc - unfinished items - AppId {appId}",
            HtmlContent = BuildHtml(items)
        };
        message.AddTo(to);

        var response = await client.SendEmailAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync(cancellationToken);
            _log.LogError("SendGrid failed for AppId {AppId}: {Body}", appId, body);
        }
        else
        {
            _log.LogInformation("Sent unprocessed-items email for AppId {AppId} to {To}", appId, to);
        }
    }

    private static string BuildHtml(IReadOnlyList<PollItem> items)
    {
        var rows = string.Join(
            "",
            items.Select(i =>
                $"<tr><td>{i.FilePath}{i.FileName}</td><td>{i.JobName}</td></tr>"));

        return $"<h3>PollProc unfinished items</h3><table border=\"1\"><tr><th>File</th><th>Job</th></tr>{rows}</table>";
    }
}
