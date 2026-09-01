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

    public static IReadOnlySet<Guid> FindUniqueVisibleMentionUserIds(
        string? markdown,
        IEnumerable<KeyValuePair<Guid, string>> users)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return new HashSet<Guid>();

        var candidates = users
            .Where(user => user.Key != Guid.Empty && !string.IsNullOrWhiteSpace(user.Value))
            .GroupBy(user => DisplayName(user.Value), StringComparer.OrdinalIgnoreCase)
            .Select(group => new MentionNameCandidate(
                group.Key,
                group.Count() == 1 ? group.Single().Key : null))
            .SelectMany(candidate => Regex.Matches(
                    markdown,
                    $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(CreateVisibleMention(candidate.Name))}(?![\p{{L}}\p{{N}}_])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(match => new VisibleMentionMatch(
                    candidate.UserId,
                    match.Index,
                    match.Length)))
            .OrderBy(match => match.Index)
            .ThenByDescending(match => match.Length)
            .ToArray();

        var selectedMatches = new List<VisibleMentionMatch>();
        foreach (var candidate in candidates)
        {
            if (selectedMatches.Any(selected =>
                    candidate.Index < selected.Index + selected.Length &&
                    selected.Index < candidate.Index + candidate.Length))
                continue;

            selectedMatches.Add(candidate);
        }

        return selectedMatches
            .Where(match => match.UserId.HasValue)
            .Select(match => match.UserId!.Value)
            .ToHashSet();
    }

    private static string DisplayName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "LUMA user" : name.Trim();

    private sealed record MentionNameCandidate(string Name, Guid? UserId);
    private sealed record VisibleMentionMatch(Guid? UserId, int Index, int Length);

    [GeneratedRegex(@"\[(?<label>@(?:\\.|[^\]\r\n]){1,160})\]\(luma-user:(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)", RegexOptions.CultureInvariant)]
    private static partial Regex MentionTokenPattern();
}
