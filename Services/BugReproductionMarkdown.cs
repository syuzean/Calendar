using System.Text;
using System.Text.RegularExpressions;
using Calendar.Models;
using Markdig;
using Markdig.Syntax;

namespace Calendar.Services;

public sealed record ParsedBugReproductionStep(int Position, string Markdown);

public static partial class BugReproductionMarkdown
{
    public const int MaximumLength = 30000;

    public static IReadOnlyList<ParsedBugReproductionStep> Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return [];
        var document = Markdown.Parse(source);
        var result = new List<ParsedBugReproductionStep>();
        foreach (var list in document.OfType<ListBlock>().Where(item => item.IsOrdered))
        {
            foreach (var item in list.OfType<ListItemBlock>())
            {
                if (item.Span.Start < 0 || item.Span.End < item.Span.Start || item.Span.End >= source.Length) continue;
                var markdown = source[item.Span.Start..(item.Span.End + 1)];
                markdown = OrderedMarker().Replace(markdown, string.Empty, 1).Trim();
                if (markdown.Length > 0) result.Add(new(result.Count, markdown));
            }
        }
        return result;
    }

    public static string FromLegacySteps(IEnumerable<BugReproductionStepDetails>? steps)
    {
        var builder = new StringBuilder();
        foreach (var step in steps?.OrderBy(item => item.Position) ?? Enumerable.Empty<BugReproductionStepDetails>())
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.Append(step.Position + 1).Append(". ").Append(IndentContinuation(step.Content));
            if (!string.IsNullOrWhiteSpace(step.ObservedResult))
                builder.AppendLine().AppendLine().Append("   **Actual:** ").Append(IndentContinuation(step.ObservedResult));
            foreach (var image in step.Images)
                builder.AppendLine().AppendLine().Append("   ![").Append(EscapeLabel(image.FileName)).Append("](").Append(image.Url).Append(')');
            builder.AppendLine();
        }
        return builder.ToString().Trim();
    }

    public static string MatchKey(string markdown) => Regex.Replace(markdown.Trim(), @"\s+", " ");

    private static string IndentContinuation(string value) => value.Trim().Replace("\r\n", "\n").Replace("\n", "\n   ");
    private static string EscapeLabel(string value) => value.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");

    [GeneratedRegex(@"^[ \t]{0,3}\d+[.)][ \t]+", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedMarker();
}
