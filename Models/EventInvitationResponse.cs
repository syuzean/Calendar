namespace Calendar.Models;

public sealed record EventInvitationResponseView(
    Guid InvitationId,
    string RecipientName,
    string RecipientEmail,
    EventInvitationStatus Status,
    string Comment,
    DateTime? RespondedUtc,
    bool CanRespond);

public sealed record EventInvitationResponseSummary(int Accepted, int Declined, int Pending)
{
    public static EventInvitationResponseSummary From(IEnumerable<EventInvitationResponseView> responses) => new(
        responses.Count(response => response.Status == EventInvitationStatus.Accepted),
        responses.Count(response => response.Status == EventInvitationStatus.Declined),
        responses.Count(response => response.Status == EventInvitationStatus.Pending));
}

public sealed record InvitationResponseRequest(EventInvitationStatus Status, string Comment);
