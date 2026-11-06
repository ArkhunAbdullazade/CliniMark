using System.Net;
using System.Net.Mail;
using CliniMark.Core.Services;
using CliniMark.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace CliniMark.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly SmtpSettings smtpSettings;

    public EmailService(IOptions<SmtpSettings> smtpSettings)
    {
        this.smtpSettings = smtpSettings.Value;
    }

    public async Task SendEmailAsync(string email, string subject, string message)
    {
        if (string.IsNullOrWhiteSpace(smtpSettings.Server) ||
            string.IsNullOrWhiteSpace(smtpSettings.SenderEmail) ||
            string.IsNullOrWhiteSpace(smtpSettings.Password))
        {
            throw new InvalidOperationException("SMTP settings must be configured before sending email.");
        }

        using var smtpClient = new SmtpClient(smtpSettings.Server, smtpSettings.Port)
        {
            UseDefaultCredentials = false,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(smtpSettings.SenderEmail, smtpSettings.Password),
            EnableSsl = smtpSettings.EnableSsl
        };

        using var mailMessage = new MailMessage(smtpSettings.SenderEmail, email)
        {
            Subject = subject,
            Body = message,
            IsBodyHtml = true,
        };

        await smtpClient.SendMailAsync(mailMessage);
    }
}
