using Ganss.Xss;
using Markdig;
using System.Text.RegularExpressions;

namespace Calendar.Services;

public interface ITaskMarkdownRenderer
{
    string RenderHtml(string? markdown, IReadOnlyCollection<string>? mentionNames = null);
}

public sealed class TaskMarkdownRenderer : ITaskMarkdownRenderer
{
    private static readonly Regex MentionLinkPattern = new(
        "<a href=\"luma-user:(?<id>[0-9a-fA-F-]{36})\">(?<label>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private readonly HtmlSanitizer sanitizer;

    public TaskMarkdownRenderer()
    {
        sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(
        [
            "p", "br", "strong", "em", "del", "blockquote", "code", "pre",
            "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "a"
        ]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "title"]);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto", TaskMentionSyntax.Scheme]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
    }

    public string RenderHtml(string? markdown, IReadOnlyCollection<string>? mentionNames = null)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var sanitized = sanitizer.Sanitize(Markdown.ToHtml(markdown, Pipeline));
        sanitized = MentionLinkPattern.Replace(
            sanitized,
            match => $"<span class=\"task-mention\">{match.Groups["label"].Value}</span>");
        if (mentionNames is null || mentionNames.Count == 0) return sanitized;

        var encodedMentions = mentionNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => System.Net.WebUtility.HtmlEncode(TaskMentionSyntax.CreateVisibleMention(name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(mention => mention.Length)
            .ToArray();
        if (encodedMentions.Length == 0) return sanitized;

        var alternatives = string.Join("|", encodedMentions.Select(Regex.Escape));
        sanitized = Regex.Replace(
            sanitized,
            $"(?<![\\p{{L}}\\p{{N}}_])(?:{alternatives})(?![\\p{{L}}\\p{{N}}_])(?![^<]*>)",
            match => $"<span class=\"task-mention\">{match.Value}</span>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return sanitized;
    }
}
