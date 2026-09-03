using Calendar.Services;
using Xunit;

namespace Calendar.Tests;

public sealed class BugReproductionMarkdownTests
{
    [Theory]
    [InlineData("1. Open app", 1)]
    [InlineData("1. Open app\n2. Login", 2)]
    [InlineData("1. One\n2. Two\n3. Three", 3)]
    [InlineData("1. One\n2. Two\n3. Three\n4. Four\n5. Five\n6. Six", 6)]
    public void Parse_ReturnsEveryTopLevelOrderedStep(string markdown, int expected)
    {
        var steps = BugReproductionMarkdown.Parse(markdown);
        Assert.Equal(expected, steps.Count);
        Assert.Equal(Enumerable.Range(0, expected), steps.Select(step => step.Position));
    }

    [Fact]
    public void Parse_KeepsNestedMarkdownInsideItsParentStep()
    {
        var markdown = "1. Open **POS**\n\n   - Use test environment\n   - Login as `qa`\n\n2. Click Pay\n\n   ```text\n   401 Unauthorized\n   ```";

        var steps = BugReproductionMarkdown.Parse(markdown);

        Assert.Equal(2, steps.Count);
        Assert.Contains("- Use test environment", steps[0].Markdown);
        Assert.Contains("**POS**", steps[0].Markdown);
        Assert.Contains("```text", steps[1].Markdown);
    }

    [Fact]
    public void Parse_KeepsMarkdownImageInTheContainingStep()
    {
        var steps = BugReproductionMarkdown.Parse("1. Click Pay\n\n   ![error](/task-attachments/123)\n\n2. Retry");

        Assert.Equal(2, steps.Count);
        Assert.Contains("![error](/task-attachments/123)", steps[0].Markdown);
        Assert.DoesNotContain("![error]", steps[1].Markdown);
    }
}
