using System.Net;
using System.Text.RegularExpressions;
using Azure;
using Azure.Communication.Email;
using CliniMark.Core.Services;
using Microsoft.Extensions.Options;

namespace CliniMark.Infrastructure.Services.Notifications;

public interface INotificationMailer
{
    bool IsConfigured { get; }
    Task SendAsync(IReadOnlyList<string> to, string subject, string html, CancellationToken cancellationToken);
}

/// <summary>The sending account (section "Email"): an Azure Communication Services connection string and sender address.</summary>
public sealed class AzureEmailSettings
{
    public const string Section = "Email";
    public string? AcsConnectionString { get; set; }
    public string? Sender { get; set; }
}

/// <summary>Sends through Azure Communication Services and waits until the service has accepted or refused the email.</summary>
public sealed partial class AcsNotificationMailer(IOptions<AzureEmailSettings> options) : INotificationMailer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private EmailClient? client;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.AcsConnectionString) && EmailFormat.IsValid(options.Value.Sender);

    public async Task SendAsync(IReadOnlyList<string> to, string subject, string html, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email is not configured: set Email:AcsConnectionString and Email:Sender.");
        client ??= new EmailClient(options.Value.AcsConnectionString);
        var message = new EmailMessage(options.Value.Sender, new EmailRecipients(to.Select(address => new EmailAddress(address)).ToList()),
            new EmailContent(subject) { Html = html, PlainText = PlainText(html) });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var operation = await client.SendAsync(WaitUntil.Completed, message, timeout.Token);
        if (operation.Value.Status != EmailSendStatus.Succeeded)
            throw new InvalidOperationException($"The email service reported {operation.Value.Status}.");
    }

    // A readable plain-text part, which some mail programs show and spam filters expect.
    public static string PlainText(string html)
    {
        var text = BlockEnd().Replace(html, "\n");
        text = Tag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return string.Join("\n", text.Split('\n').Select(line => Spaces().Replace(line, " ").Trim()).Where(line => line.Length > 0));
    }

    [GeneratedRegex(@"<(br|/p|/tr|/h1|/h2|/h3|/div|/li|hr)[^>]*>", RegexOptions.IgnoreCase)] private static partial Regex BlockEnd();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex(@"[ \t\r]+")] private static partial Regex Spaces();
}
