using System.Text.RegularExpressions;

namespace Calendar.Services;

public sealed record ParsedTaskMention(Guid UserId, string Token);

public static partial class TaskMentionSyntax
{
    public const string Scheme = "luma-user";

    public static IReadOnlyList<ParsedTaskMention> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        return MentionTokenPattern().Matches(markdown).Cast<Match>()
            .Select(match => new ParsedTaskMention(
                Guid.Parse(match.Groups["id"].Value),
                match.Value))
            .ToArray();
    }

    public static string Canonicalize(string? markdown, IReadOnlyDictionary<Guid, string> userNames)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        return MentionTokenPattern().Replace(markdown, match =>
        {
            var userId = Guid.Parse(match.Groups["id"].Value);
            return userNames.TryGetValue(userId, out var name)
                ? CreateVisibleMention(name)
                : match.Value;
        });
    }

    public static string CreateVisibleMention(string name) => $"@{DisplayName(name)}";

    public static string CreateLegacyToken(Guid userId, string name) =>
        $"[@{DisplayName(name)}]({Scheme}:{userId:D})";

    public static bool ContainsVisibleMention(string markdown, string name) =>
        Regex.IsMatch(
            markdown,
            $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(CreateVisibleMention(name))}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string DisplayName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "LUMA user" : name.Trim();

    [GeneratedRegex(@"\[(?<label>@(?:\\.|[^\]\r\n]){1,160})\]\(luma-user:(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)", RegexOptions.CultureInvariant)]
    private static partial Regex MentionTokenPattern();
}
