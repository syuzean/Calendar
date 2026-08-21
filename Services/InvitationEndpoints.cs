using System.Security.Claims;

namespace Calendar.Services;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/invitation", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        string? token,
        IEventInvitationService invitationService)
    {
        token ??= string.Empty;
        var inspection = await invitationService.InspectAsync(token, context.RequestAborted);
        if (inspection.Status != InvitationStatus.Valid)
            return ErrorRedirect(context.User.Identity?.IsAuthenticated == true,
                inspection.Status == InvitationStatus.Expired
                    ? "This invitation has expired. Ask the organizer to share the event again."
                    : "This invitation is invalid or is no longer available.");

        var invitationUrl = InvitationFlow.InvitationUrl(token);
        if (context.User.Identity?.IsAuthenticated != true)
            return Results.LocalRedirect(InvitationFlow.LoginUrl(invitationUrl, token));

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return ErrorRedirect(true, "Your account could not be verified for this invitation.");

        var claim = await invitationService.ClaimAsync(token, userId, context.RequestAborted);
        return claim.Status switch
        {
            InvitationClaimStatus.Success => Results.LocalRedirect($"/?event={claim.EventId:D}"),
            InvitationClaimStatus.Expired => ErrorRedirect(true, "This invitation has expired. Ask the organizer to share the event again."),
            InvitationClaimStatus.EmailMismatch => ErrorRedirect(true, "This invitation belongs to a different email address."),
            _ => ErrorRedirect(true, "This invitation is invalid or is no longer available.")
        };
    }

    private static IResult ErrorRedirect(bool authenticated, string message)
    {
        var encoded = Uri.EscapeDataString(message);
        return authenticated
            ? Results.LocalRedirect($"/?invitationError={encoded}")
            : Results.LocalRedirect($"/login?error={encoded}");
    }
}
