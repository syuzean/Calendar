using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Calendar.Services.Email;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("SMTP email delivery is not configured.");
        if (string.IsNullOrWhiteSpace(_options.Host) || string.IsNullOrWhiteSpace(_options.FromAddress))
            throw new InvalidOperationException("SMTP host and sender address must be configured.");
        if (string.IsNullOrWhiteSpace(_options.UserName) || string.IsNullOrWhiteSpace(_options.Password))
            throw new InvalidOperationException("SMTP username and password must be configured.");

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.RecipientAddress));
        mimeMessage.Subject = message.Subject;
        var bodyBuilder = new BodyBuilder { TextBody = message.PlainTextBody };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
            bodyBuilder.HtmlBody = message.HtmlBody;
        mimeMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.Security,
            cancellationToken);
        await client.AuthenticateAsync(_options.UserName, _options.Password, cancellationToken);
        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

}

public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";
    public const string PasswordEnvironmentVariable = "Email__Smtp__Password";

    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool Security { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "LUMA Calendar";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
