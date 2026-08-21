namespace Calendar.Services.Email;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    string RecipientAddress,
    string Subject,
    string PlainTextBody,
    string? HtmlBody = null)
{
    public string Body => PlainTextBody;
}
