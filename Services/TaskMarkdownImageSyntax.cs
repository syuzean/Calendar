namespace Calendar.Services;

public static class TaskMarkdownImageSyntax
{
    private const string PendingScheme = "luma-task-image:";
    private static readonly System.Text.RegularExpressions.Regex EmbeddedDataImagePattern = new(
        @"!\[[^\]]*\]\(\s*data\s*:\s*image/",
        System.Text.RegularExpressions.RegexOptions.Compiled |
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string CreatePendingUrl(string token) =>
        PendingScheme + NormalizeToken(token);

    public static bool ContainsEmbeddedDataImage(string? markdown) =>
        !string.IsNullOrWhiteSpace(markdown) && EmbeddedDataImagePattern.IsMatch(markdown);

    public static string CreateMarkdown(string fileName, string token)
    {
        var label = Path.GetFileName(fileName)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
        return $"![{label}]({CreatePendingUrl(token)})";
    }

    public static string ResolvePendingUrls(
        string description,
        IReadOnlyList<TaskAttachmentUpload>? uploads,
        IReadOnlyList<Models.TaskAttachment> storedAttachments)
    {
        if (string.IsNullOrEmpty(description) || uploads is null || uploads.Count == 0)
            return description;
        if (uploads.Count != storedAttachments.Count)
            throw new InvalidOperationException("Stored task images do not match the submitted uploads.");

        for (var index = 0; index < uploads.Count; index++)
        {
            var token = uploads[index].InlineToken;
            if (string.IsNullOrWhiteSpace(token)) continue;
            description = description.Replace(
                CreatePendingUrl(token),
                $"/task-attachments/{storedAttachments[index].Id:D}",
                StringComparison.Ordinal);
        }

        return description;
    }

    private static string NormalizeToken(string token)
    {
        if (!Guid.TryParseExact(token, "N", out var parsed))
            throw new ArgumentException("Task image token is invalid.", nameof(token));
        return parsed.ToString("N");
    }
}
