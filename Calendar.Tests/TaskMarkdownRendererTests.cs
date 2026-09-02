using Calendar.Services;
using Xunit;

namespace Calendar.Tests;

public sealed class TaskMarkdownRendererTests
{
    private readonly TaskMarkdownRenderer renderer = new();

    [Fact]
    public void RenderHtml_RendersCommonMarkdown()
    {
        var html = renderer.RenderHtml(
            "## Context\n\nUse **safe Markdown** with *emphasis*.\n\n" +
            "- First\n- Second\n\n1. Ordered\n2. List\n\n" +
            "[Open LUMA](https://luma.example/tasks)\n\n`inline code`\n\n```text\ncode block\n```");

        Assert.Contains("<h2>Context</h2>", html);
        Assert.Contains("<strong>safe Markdown</strong>", html);
        Assert.Contains("<em>emphasis</em>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("href=\"https://luma.example/tasks\"", html);
        Assert.Contains("<code>inline code</code>", html);
        Assert.Contains("<pre><code>code block", html);
    }

    [Fact]
    public void RenderHtml_RendersSingleNewlinesAsVisibleLineBreaks()
    {
        var html = renderer.RenderHtml("First line\nSecond line");

        Assert.Matches("First line<br\\s*/?>\\s*Second line", html);
    }

    [Fact]
    public void RenderHtml_DoesNotRenderUnsafeRawHtmlOrUrlSchemes()
    {
        var html = renderer.RenderHtml(
            "<script>alert('x')</script><img src=x onerror=alert(1)>\n\n[Unsafe](javascript:alert(1))");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderHtml_AllowsSafeHttpsLinks()
    {
        var html = renderer.RenderHtml("[LUMA](https://luma.example/tasks)");

        Assert.Contains("href=\"https://luma.example/tasks\"", html);
    }

    [Fact]
    public void RenderHtml_RendersExternalAndLumaHostedMarkdownImages()
    {
        var attachmentId = Guid.NewGuid();
        var html = renderer.RenderHtml(
            $"![Architecture diagram](https://images.example/diagram.png)\n\n" +
            $"![Task screenshot](/task-attachments/{attachmentId:D})");

        Assert.Contains("<img", html);
        Assert.Contains("src=\"https://images.example/diagram.png\"", html);
        Assert.Contains($"src=\"/task-attachments/{attachmentId:D}\"", html);
        Assert.Contains("alt=\"Architecture diagram\"", html);
    }

    [Fact]
    public void RenderHtml_RejectsUnsafeMarkdownImageSources()
    {
        var html = renderer.RenderHtml(
            "![Unsafe](javascript:alert(1))\n\n![Embedded](data:image/png;base64,AAAA)");

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:image", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderHtml_RendersLumaMentionAsStyledNonNavigatingText()
    {
        var html = renderer.RenderHtml(
            TaskMentionSyntax.CreateVisibleMention("Anna Smith"),
            ["Anna Smith"]);

        Assert.Contains("class=\"task-mention\"", html);
        Assert.Contains("@Anna Smith", html);
        Assert.DoesNotContain("luma-user:", html);
        Assert.DoesNotContain("<a", html);
    }

    [Fact]
    public void RenderHtml_HidesLegacyMentionIdentifiersFromRenderedDetails()
    {
        var userId = Guid.NewGuid();
        var html = renderer.RenderHtml(
            TaskMentionSyntax.CreateLegacyToken(userId, "Anna Smith"));

        Assert.Contains("class=\"task-mention\"", html);
        Assert.Contains("@Anna Smith", html);
        Assert.DoesNotContain(userId.ToString(), html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("luma-user:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderHtml_DistinguishesOverlappingMentionNames()
    {
        var html = renderer.RenderHtml("@Anna and @Anna Smith", ["Anna", "Anna Smith"]);

        Assert.Equal(2, html.Split("class=\"task-mention\"").Length - 1);
        Assert.Contains(">@Anna</span>", html);
        Assert.Contains("@Anna Smith</span>", html);
    }
}
