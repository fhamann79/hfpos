using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;

namespace Pos.Backend.Api.Infrastructure.Services;

public class SmtpEmailSenderService : IEmailSenderService
{
    public async Task SendAsync(
        CompanyEmailSettings settings,
        string smtpPassword,
        OutboundEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        var mimeMessage = BuildMessage(settings, message);

        using var client = new SmtpClient
        {
            Timeout = 30000
        };

        await client.ConnectAsync(
            settings.SmtpHost!,
            settings.SmtpPort,
            ResolveSocketOptions(settings.EncryptionMode),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername)
            || !string.IsNullOrWhiteSpace(smtpPassword))
        {
            await client.AuthenticateAsync(
                settings.SmtpUsername ?? string.Empty,
                smtpPassword,
                cancellationToken);
        }

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static MimeMessage BuildMessage(CompanyEmailSettings settings, OutboundEmailMessage message)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(settings.FromDisplayName ?? string.Empty, settings.FromEmail!));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        if (!string.IsNullOrWhiteSpace(settings.ReplyToEmail))
        {
            mimeMessage.ReplyTo.Add(MailboxAddress.Parse(settings.ReplyToEmail));
        }

        var body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        };

        foreach (var attachment in message.Attachments)
        {
            body.Attachments.Add(
                attachment.FileName,
                attachment.Bytes,
                ContentType.Parse(attachment.ContentType));
        }

        mimeMessage.Body = body.ToMessageBody();

        return mimeMessage;
    }

    private static SecureSocketOptions ResolveSocketOptions(string encryptionMode)
        => encryptionMode switch
        {
            "None" => SecureSocketOptions.None,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls
        };
}
