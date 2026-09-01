using Calendar.Services;
using Xunit;

namespace Calendar.Tests;

public sealed class TaskMarkdownRendererTests
{
    private readonly TaskMarkdownRenderer renderer = new();

    [Fact]
    public void RenderHtml_RendersCommonMarkdown()
    {
        var html = renderer.RenderHtml("## Context\n\nUse **safe Markdown**.\n\n- First\n- Second\n\n`code`");

        Assert.Contains("<h2>Context</h2>", html);
        Assert.Contains("<strong>safe Markdown</strong>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<code>code</code>", html);
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
    public void RenderHtml_DistinguishesOverlappingMentionNames()
    {
        var html = renderer.RenderHtml("@Anna and @Anna Smith", ["Anna", "Anna Smith"]);

        Assert.Equal(2, html.Split("class=\"task-mention\"").Length - 1);
        Assert.Contains(">@Anna</span>", html);
        Assert.Contains("@Anna Smith</span>", html);
    }
}
