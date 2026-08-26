using Microsoft.Extensions.Options;

namespace Calendar.Services.Email;

public interface ITaskLinkBuilder
{
    string Task(Guid taskId);
    string Invitation(string token);
}

public sealed class TaskLinkBuilder(IOptions<EmailLinkOptions> options) : ITaskLinkBuilder
{
    private readonly Uri _baseUri = CreateBaseUri(options.Value.PublicBaseUrl);

    public string Task(Guid taskId) => new Uri(_baseUri, $"tasks?task={taskId:D}").AbsoluteUri;
    public string Invitation(string token) =>
        new Uri(_baseUri, $"task-invitation?token={Uri.EscapeDataString(token)}").AbsoluteUri;

    private static Uri CreateBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Email PublicBaseUrl must be an absolute HTTP or HTTPS URL.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + '/');
    }
}
