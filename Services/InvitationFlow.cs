using Microsoft.AspNetCore.WebUtilities;

namespace Calendar.Services;

public static class InvitationFlow
{
    public static async Task<InvitationLoginContext> ResolveLoginContextAsync(
        IEventInvitationService invitationService,
        string? invitationToken,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var token = EffectiveToken(invitationToken, returnUrl);
        if (string.IsNullOrWhiteSpace(token))
            return new(false, null, returnUrl);

        var inspection = await invitationService.InspectAsync(token, cancellationToken);
        return inspection.Status == InvitationStatus.Valid
            ? new(true, token, InvitationUrl(token))
            : new(false, null, returnUrl);
    }

    public static string? EffectiveToken(string? invitationToken, string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(invitationToken)) return invitationToken;
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/invitation?", StringComparison.Ordinal))
            return null;

        var query = QueryHelpers.ParseQuery(returnUrl[(returnUrl.IndexOf('?') + 1)..]);
        return query.TryGetValue("token", out var value) && value.Count == 1
            ? value[0]
            : null;
    }

    public static string InvitationUrl(string token) => $"/invitation?token={Uri.EscapeDataString(token)}";
    public static string GuestUrl(string token) => $"/guest-event?token={Uri.EscapeDataString(token)}";

    public static string LoginUrl(string? returnUrl, string? invitationToken = null) =>
        AuthenticationUrl("/login", returnUrl, invitationToken);

    public static string RegisterUrl(string? returnUrl, string? invitationToken = null) =>
        AuthenticationUrl("/register", returnUrl, invitationToken);

    private static string AuthenticationUrl(string path, string? returnUrl, string? invitationToken)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(returnUrl))
            values.Add($"returnUrl={Uri.EscapeDataString(returnUrl)}");
        if (!string.IsNullOrWhiteSpace(invitationToken))
            values.Add($"invitationToken={Uri.EscapeDataString(invitationToken)}");
        return values.Count == 0 ? path : $"{path}?{string.Join('&', values)}";
    }
}

public sealed record InvitationLoginContext(bool ShowGuestOption, string? Token, string? ReturnUrl);
