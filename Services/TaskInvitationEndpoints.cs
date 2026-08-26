using System.Security.Claims;

namespace Calendar.Services;

public static class TaskInvitationEndpoints
{
    public static IEndpointRouteBuilder MapTaskInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/task-invitation", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        string? token,
        ITaskInvitationService invitationService)
    {
        token ??= string.Empty;
        var inspection = await invitationService.InspectAsync(token, context.RequestAborted);
        if (inspection.Status != TaskInvitationAccessStatus.Valid)
            return ErrorRedirect(
                context.User.Identity?.IsAuthenticated == true,
                inspection.Status == TaskInvitationAccessStatus.Expired
                    ? "This task invitation has expired. Ask the Task Maker to assign it again."
                    : "This task invitation is invalid or has already been used.");

        var invitationUrl = $"/task-invitation?token={Uri.EscapeDataString(token)}";
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Results.LocalRedirect(
                $"/login?returnUrl={Uri.EscapeDataString(invitationUrl)}");
        }

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return ErrorRedirect(true, "Your account could not be verified for this task invitation.");

        var claim = await invitationService.ClaimAsync(token, userId, context.RequestAborted);
        return claim.Status switch
        {
            TaskInvitationClaimStatus.Success => Results.LocalRedirect($"/tasks?task={claim.TaskId:D}"),
            TaskInvitationClaimStatus.Expired => ErrorRedirect(true, "This task invitation has expired. Ask the Task Maker to assign it again."),
            TaskInvitationClaimStatus.EmailMismatch => ErrorRedirect(true, "This task invitation belongs to a different email address."),
            _ => ErrorRedirect(true, "This task invitation is invalid or has already been used.")
        };
    }

    private static IResult ErrorRedirect(bool authenticated, string message)
    {
        var encoded = Uri.EscapeDataString(message);
        return authenticated
            ? Results.LocalRedirect($"/tasks?taskInvitationError={encoded}")
            : Results.LocalRedirect($"/login?error={encoded}");
    }
}
