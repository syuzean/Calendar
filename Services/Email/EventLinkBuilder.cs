using Microsoft.Extensions.Options;

namespace Calendar.Services.Email;

public interface IEventLinkBuilder
{
    string Event(Guid eventId);
    string Invitation(string token);
    string Invitation(Guid invitationId);
}

public sealed class EventLinkBuilder(
    IOptions<EmailLinkOptions> options,
    IInvitationAccessTokenService invitationAccessTokens) : IEventLinkBuilder
{
    private readonly Uri _baseUri = CreateBaseUri(options.Value.PublicBaseUrl);

    public string Event(Guid eventId) => new Uri(_baseUri, $"?event={eventId:D}").AbsoluteUri;

    public string Invitation(string token) =>
        new Uri(_baseUri, $"invitation?token={Uri.EscapeDataString(token)}").AbsoluteUri;

    public string Invitation(Guid invitationId) => Invitation(invitationAccessTokens.Create(invitationId));

    private static Uri CreateBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Email PublicBaseUrl must be an absolute HTTP or HTTPS URL.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + '/');
    }
}

public sealed class EmailLinkOptions
{
    public const string SectionName = "Email";
    public string PublicBaseUrl { get; set; } = string.Empty;
}
