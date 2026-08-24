using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Calendar.Services.Email;

public sealed partial class FileEmailTemplateRenderer(string templateDirectory) : IEmailTemplateRenderer
{
    public RenderedEmailTemplate RenderEventShared(EventSharedTemplateData data)
    {
        var values = EventValues(
            data.RecipientName, data.RecipientEmail, data.OrganizerName, data.EventTitle,
            data.Start, data.End, data.IsAllDay, data.Description, data.EventColor,
            data.EventUrl, data.MeetingUrl, data.MeetingProvider);
        return RenderTemplate("EventShared", values);
    }

    public RenderedEmailTemplate RenderEventUpdated(EventUpdatedTemplateData data)
    {
        var values = EventValues(
            data.RecipientName, data.RecipientEmail, data.OrganizerName, data.EventTitle,
            data.Start, data.End, data.IsAllDay, data.Description, data.EventColor,
            data.EventUrl, data.MeetingUrl, data.MeetingProvider);
        values["ChangedFields"] = data.ChangedFields;
        return RenderTemplate("EventUpdated", values);
    }

    public RenderedEmailTemplate RenderEventCancelled(EventCancelledTemplateData data)
    {
        var values = EventValues(
            data.RecipientName, data.RecipientEmail, data.OrganizerName, data.EventTitle,
            data.Start, data.End, data.IsAllDay, string.Empty, data.EventColor,
            string.Empty, string.Empty, string.Empty);
        return RenderTemplate("EventCancelled", values);
    }

    private RenderedEmailTemplate RenderTemplate(string templateName, IReadOnlyDictionary<string, string> values)
    {
        var subject = Render(Read($"{templateName}.subject.txt"), values, htmlEncode: false)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
        var html = Render(Read($"{templateName}.html"), values, htmlEncode: true).Trim();
        var text = Render(Read($"{templateName}.txt"), values, htmlEncode: false).Trim();
        return new RenderedEmailTemplate(subject, html, text);
    }

    private string Read(string fileName) => File.ReadAllText(Path.Combine(templateDirectory, fileName));

    private static Dictionary<string, string> EventValues(
        string recipientName,
        string recipientEmail,
        string organizerName,
        string eventTitle,
        DateTime start,
        DateTime end,
        bool isAllDay,
        string description,
        string eventColor,
        string eventUrl,
        string meetingUrl,
        string meetingProvider)
    {
        var finalDate = isAllDay ? end.AddDays(-1) : end;
        var eventDate = start.Date == finalDate.Date
            ? start.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
            : $"{start:MMMM d, yyyy} – {finalDate:MMMM d, yyyy}";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RecipientName"] = string.IsNullOrWhiteSpace(recipientName) ? recipientEmail : recipientName,
            ["RecipientEmail"] = recipientEmail,
            ["OrganizerName"] = organizerName,
            ["EventTitle"] = eventTitle,
            ["EventDate"] = eventDate,
            ["StartTime"] = isAllDay ? "All day" : start.ToString("h:mm tt", CultureInfo.InvariantCulture),
            ["EndTime"] = isAllDay ? "" : end.ToString("h:mm tt", CultureInfo.InvariantCulture),
            ["Description"] = description ?? string.Empty,
            ["EventColor"] = eventColor,
            ["EventUrl"] = eventUrl,
            ["MeetingUrl"] = meetingUrl,
            ["MeetingProvider"] = meetingProvider
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
