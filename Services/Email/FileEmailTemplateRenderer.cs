using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Calendar.Services.Email;

public sealed partial class FileEmailTemplateRenderer(string templateDirectory) : IEmailTemplateRenderer
{
    public RenderedEmailTemplate RenderEventShared(EventSharedTemplateData data)
    {
        var values = Values(data);
        var subject = Render(Read("EventShared.subject.txt"), values, htmlEncode: false)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
        var html = Render(Read("EventShared.html"), values, htmlEncode: true).Trim();
        var text = Render(Read("EventShared.txt"), values, htmlEncode: false).Trim();
        return new RenderedEmailTemplate(subject, html, text);
    }

    private string Read(string fileName) => File.ReadAllText(Path.Combine(templateDirectory, fileName));

    private static Dictionary<string, string> Values(EventSharedTemplateData data)
    {
        var finalDate = data.IsAllDay ? data.End.AddDays(-1) : data.End;
        var eventDate = data.Start.Date == finalDate.Date
            ? data.Start.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
            : $"{data.Start:MMMM d, yyyy} – {finalDate:MMMM d, yyyy}";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RecipientName"] = string.IsNullOrWhiteSpace(data.RecipientName) ? data.RecipientEmail : data.RecipientName,
            ["RecipientEmail"] = data.RecipientEmail,
            ["OrganizerName"] = data.OrganizerName,
            ["EventTitle"] = data.EventTitle,
            ["EventDate"] = eventDate,
            ["StartTime"] = data.IsAllDay ? "All day" : data.Start.ToString("h:mm tt", CultureInfo.InvariantCulture),
            ["EndTime"] = data.IsAllDay ? "" : data.End.ToString("h:mm tt", CultureInfo.InvariantCulture),
            ["Description"] = data.Description ?? string.Empty,
            ["EventColor"] = data.EventColor,
            ["EventUrl"] = data.EventUrl,
            ["MeetingUrl"] = data.MeetingUrl,
            ["MeetingProvider"] = data.MeetingProvider
        };
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> values, bool htmlEncode)
    {
        template = ConditionalPattern().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value)
                ? match.Groups[2].Value
                : string.Empty);
        return VariablePattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
                throw new InvalidOperationException($"Email template contains an unknown variable: {name}.");
            if (!htmlEncode) return value;
            var encoded = WebUtility.HtmlEncode(value);
            return name == "Description"
                ? encoded.Replace("\r\n", "<br>").Replace("\n", "<br>")
                : encoded;
        });
    }

    [GeneratedRegex(@"\{\{#([A-Za-z][A-Za-z0-9]*)\}\}([\s\S]*?)\{\{/\1\}\}")]
    private static partial Regex ConditionalPattern();

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9]*)\}\}")]
    private static partial Regex VariablePattern();
}
