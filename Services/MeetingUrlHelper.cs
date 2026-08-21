namespace Calendar.Services;

public static class MeetingUrlHelper
{
    public const int MaximumLength = 2048;

    public static bool TryNormalize(string? value, out string normalized, out string? error)
    {
        normalized = value?.Trim() ?? string.Empty;
        error = null;
        if (normalized.Length == 0) return true;
        if (normalized.Length > MaximumLength)
        {
            error = $"Meeting link cannot exceed {MaximumLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Enter a valid http or https meeting link. HTTPS is recommended.";
            return false;
        }

        return true;
    }

    public static bool TryGetSafeUri(string? value, out Uri? uri)
    {
        uri = null;
        return TryNormalize(value, out var normalized, out _) &&
            normalized.Length > 0 && Uri.TryCreate(normalized, UriKind.Absolute, out uri);
    }

    public static string ProviderName(string? value)
    {
        if (!TryGetSafeUri(value, out var uri) || uri is null) return "Online meeting";
        var host = uri.Host;
        if (IsHost(host, "meet.google.com")) return "Google Meet";
        if (IsHost(host, "zoom.us") || IsHost(host, "zoom.com") || IsHost(host, "zoomgov.com")) return "Zoom";
        if (IsHost(host, "teams.microsoft.com") || IsHost(host, "teams.live.com") ||
            IsHost(host, "teams.cloud.microsoft") || IsHost(host, "msteams.link")) return "Microsoft Teams";
        return "Online meeting";
    }

    public static string JoinLabel(string? value) => ProviderName(value) switch
    {
        "Google Meet" => "Join Google Meet",
        "Zoom" => "Join Zoom",
        "Microsoft Teams" => "Join Microsoft Teams",
        _ => "Join meeting"
    };

    private static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);
}
