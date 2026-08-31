using Ganss.Xss;
using Markdig;

namespace Calendar.Services;

public interface ITaskMarkdownRenderer
{
    string RenderHtml(string? markdown);
}

public sealed class TaskMarkdownRenderer : ITaskMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
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
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
    }

    public string RenderHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        return sanitizer.Sanitize(Markdown.ToHtml(markdown, Pipeline));
    }
}
