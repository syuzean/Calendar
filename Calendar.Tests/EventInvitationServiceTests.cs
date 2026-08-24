using System.Security.Cryptography;
using System.Text;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calendar.Tests;

public sealed class EventInvitationServiceTests
{
    [Fact]
    public async Task NewInvitation_StartsPending()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");

        await using var db = fixture.CreateDbContext();
        Assert.Equal(EventInvitationStatus.Pending, (await db.EventInvitations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Guest_CanAcceptWithOptionalComment()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");

        var result = await fixture.Service.RespondAsGuestAsync(
            fixture.Token, EventInvitationStatus.Accepted, "I'll join 10 minutes later.");

        Assert.Equal(InvitationResponseResultStatus.Success, result.Status);
        Assert.Equal(EventInvitationStatus.Accepted, result.ResponseStatus);
        await using var db = fixture.CreateDbContext();
        var invitation = await db.EventInvitations.SingleAsync();
        Assert.Equal(EventInvitationStatus.Accepted, invitation.Status);
        Assert.Equal("I'll join 10 minutes later.", invitation.ResponseComment);
        Assert.NotNull(invitation.ResponseUtc);
    }

    [Fact]
    public async Task Guest_CanDeclineWithoutComment()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");

        var result = await fixture.Service.RespondAsGuestAsync(
            fixture.Token, EventInvitationStatus.Declined, null);

        Assert.Equal(InvitationResponseResultStatus.Success, result.Status);
        await using var db = fixture.CreateDbContext();
        var invitation = await db.EventInvitations.SingleAsync();
        Assert.Equal(EventInvitationStatus.Declined, invitation.Status);
        Assert.Equal(string.Empty, invitation.ResponseComment);
        Assert.NotNull(invitation.ResponseUtc);
    }

    [Fact]
    public async Task Guest_CanChangeAnExistingResponse()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        await fixture.Service.RespondAsGuestAsync(
            fixture.Token, EventInvitationStatus.Accepted, "See you there.");

        var changed = await fixture.Service.RespondAsGuestAsync(
            fixture.Token, EventInvitationStatus.Declined, "Plans changed.");

        Assert.Equal(InvitationResponseResultStatus.Success, changed.Status);
        await using var db = fixture.CreateDbContext();
        var invitation = await db.EventInvitations.SingleAsync();
        Assert.Equal(EventInvitationStatus.Declined, invitation.Status);
        Assert.Equal("Plans changed.", invitation.ResponseComment);
    }

    [Fact]
    public async Task RegisteredRecipient_CanRespondAfterInvitationClaim()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var user = fixture.NewUser("future@luma.test", "Future User");
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        Assert.Equal(InvitationClaimStatus.Success, (await fixture.Service.ClaimAsync(fixture.Token, user.Id)).Status);

        var response = await fixture.Service.RespondAsUserAsync(
            fixture.Event.Id, user.Id, EventInvitationStatus.Accepted, "Looking forward to it.");

        Assert.Equal(InvitationResponseResultStatus.Success, response.Status);
        await using var verification = fixture.CreateDbContext();
        var invitation = await verification.EventInvitations.SingleAsync();
        Assert.Equal(EventInvitationStatus.Accepted, invitation.Status);
        Assert.Equal("Looking forward to it.", invitation.ResponseComment);
    }

    [Fact]
    public async Task DifferentUser_CannotRespondToInvitation()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var other = fixture.NewUser("someone-else@luma.test", "Other User");
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(other);
            db.EventParticipants.Add(new EventParticipant { EventId = fixture.Event.Id, UserId = other.Id });
            await db.SaveChangesAsync();
        }

        var response = await fixture.Service.RespondAsUserAsync(
            fixture.Event.Id, other.Id, EventInvitationStatus.Accepted, null);

        Assert.Equal(InvitationResponseResultStatus.NotAuthorized, response.Status);
        await using var verification = fixture.CreateDbContext();
        Assert.Equal(EventInvitationStatus.Pending, (await verification.EventInvitations.SingleAsync()).Status);
    }

    [Fact]
    public async Task MultipleRecipients_KeepIndependentResponses()
    {
        var fixture = await Fixture.CreateAsync("anna@luma.test");
        const string secondToken = "second-secure-token";
        await using (var db = fixture.CreateDbContext())
        {
            db.EventInvitations.Add(new EventInvitation
            {
                EventId = fixture.Event.Id,
                RecipientEmail = "david@luma.test",
                NormalizedRecipientEmail = "DAVID@LUMA.TEST",
                Status = EventInvitationStatus.Pending,
                TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secondToken))),
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(14)
            });
            await db.SaveChangesAsync();
        }

        await fixture.Service.RespondAsGuestAsync(fixture.Token, EventInvitationStatus.Accepted, "Yes");
        await fixture.Service.RespondAsGuestAsync(secondToken, EventInvitationStatus.Declined, "No");

        await using var verification = fixture.CreateDbContext();
        var invitations = await verification.EventInvitations.OrderBy(item => item.RecipientEmail).ToListAsync();
        Assert.Equal(EventInvitationStatus.Accepted, invitations[0].Status);
        Assert.Equal("Yes", invitations[0].ResponseComment);
        Assert.Equal(EventInvitationStatus.Declined, invitations[1].Status);
        Assert.Equal("No", invitations[1].ResponseComment);
    }

    [Fact]
    public async Task ExpiredInvitation_CannotSubmitGuestResponse()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test", expiresUtc: DateTime.UtcNow.AddMinutes(-1));

        var result = await fixture.Service.RespondAsGuestAsync(
            fixture.Token, EventInvitationStatus.Accepted, null);

        Assert.Equal(InvitationResponseResultStatus.Expired, result.Status);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(EventInvitationStatus.Pending, (await db.EventInvitations.SingleAsync()).Status);
    }

    [Fact]
    public async Task NormalLoginContext_DoesNotShowGuestAccess()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");

        var context = await InvitationFlow.ResolveLoginContextAsync(fixture.Service, null, null);

        Assert.False(context.ShowGuestOption);
        Assert.Null(context.Token);
    }

    [Fact]
    public async Task ValidInvitationLoginContext_ShowsGuestAccess()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");

        var context = await InvitationFlow.ResolveLoginContextAsync(fixture.Service, fixture.Token, null);

        Assert.True(context.ShowGuestOption);
        Assert.Equal(fixture.Token, context.Token);
        Assert.Equal(InvitationFlow.InvitationUrl(fixture.Token), context.ReturnUrl);
        Assert.Contains(Uri.EscapeDataString(fixture.Token), InvitationFlow.GuestUrl(context.Token!));
    }

    [Fact]
    public async Task EventUpdatedAccessLink_ShowsGuestOption()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var updatedToken = fixture.UpdatedToken;

        var context = await InvitationFlow.ResolveLoginContextAsync(
            fixture.Service,
            updatedToken,
            InvitationFlow.InvitationUrl(updatedToken));

        Assert.True(context.ShowGuestOption);
        Assert.Equal(updatedToken, context.Token);
        Assert.Equal(InvitationFlow.InvitationUrl(updatedToken), context.ReturnUrl);
    }

    [Fact]
    public async Task EventUpdatedGuestLink_ReturnsLatestEventData()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        await using (var db = fixture.CreateDbContext())
        {
            var calendarEvent = await db.Events.SingleAsync();
            calendarEvent.Title = "Updated planning session";
            calendarEvent.Start = new DateTime(2026, 10, 21, 15, 0, 0);
            calendarEvent.End = new DateTime(2026, 10, 21, 16, 0, 0);
            calendarEvent.MeetingUrl = "https://zoom.us/j/987654321";
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.GetGuestEventAsync(fixture.UpdatedToken);

        var sharedEvent = Assert.IsType<GuestEventView>(result.Event);
        Assert.Equal(InvitationStatus.Valid, result.Status);
        Assert.Equal("Updated planning session", sharedEvent.Title);
        Assert.Equal(new DateTime(2026, 10, 21, 15, 0, 0), sharedEvent.Start);
        Assert.Equal("https://zoom.us/j/987654321", sharedEvent.MeetingUrl);
    }

    [Fact]
    public async Task UpdatedScheduleWithPendingRsvp_CanBeAnsweredByGuest()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        await fixture.Service.RespondAsGuestAsync(
            fixture.Token, EventInvitationStatus.Accepted, "Original response");
        await using (var db = fixture.CreateDbContext())
        {
            var calendarEvent = await db.Events.SingleAsync();
            calendarEvent.Start = calendarEvent.Start.AddHours(2);
            calendarEvent.End = calendarEvent.End.AddHours(2);
            var invitation = await db.EventInvitations.SingleAsync();
            invitation.Status = EventInvitationStatus.Pending;
            invitation.ResponseComment = string.Empty;
            invitation.ResponseUtc = null;
            await db.SaveChangesAsync();
        }

        var response = await fixture.Service.RespondAsGuestAsync(
            fixture.UpdatedToken, EventInvitationStatus.Accepted, "The new time works.");

        Assert.Equal(InvitationResponseResultStatus.Success, response.Status);
        await using var verification = fixture.CreateDbContext();
        var saved = await verification.EventInvitations.SingleAsync();
        Assert.Equal(EventInvitationStatus.Accepted, saved.Status);
        Assert.Equal("The new time works.", saved.ResponseComment);
    }

    [Fact]
    public async Task RevokedUpdatedAccessLink_DoesNotAllowGuestAccess()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var updatedToken = fixture.UpdatedToken;
        await using (var db = fixture.CreateDbContext())
        {
            (await db.EventInvitations.SingleAsync()).Status = EventInvitationStatus.Revoked;
            await db.SaveChangesAsync();
        }

        var inspection = await fixture.Service.InspectAsync(updatedToken);
        var guest = await fixture.Service.GetGuestEventAsync(updatedToken);

        Assert.Equal(InvitationStatus.Invalid, inspection.Status);
        Assert.Equal(InvitationStatus.Invalid, guest.Status);
        Assert.Null(guest.Event);
    }

    [Fact]
    public async Task SignInUrl_PreservesUpdatedEventInvitationContext()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var updatedToken = fixture.UpdatedToken;
        var returnUrl = InvitationFlow.InvitationUrl(updatedToken);

        var loginUrl = InvitationFlow.LoginUrl(returnUrl, updatedToken);

        Assert.Contains($"returnUrl={Uri.EscapeDataString(returnUrl)}", loginUrl);
        Assert.Contains($"invitationToken={Uri.EscapeDataString(updatedToken)}", loginUrl);
        Assert.Equal(updatedToken, InvitationFlow.EffectiveToken(null, returnUrl));
        Assert.Equal(InvitationStatus.Valid, (await fixture.Service.InspectAsync(updatedToken)).Status);
    }

    [Fact]
    public async Task RegistrationUrl_PreservesUpdatedEventInvitationContext()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var updatedToken = fixture.UpdatedToken;
        var returnUrl = InvitationFlow.InvitationUrl(updatedToken);

        var registerUrl = InvitationFlow.RegisterUrl(returnUrl, updatedToken);

        Assert.Contains($"returnUrl={Uri.EscapeDataString(returnUrl)}", registerUrl);
        Assert.Contains($"invitationToken={Uri.EscapeDataString(updatedToken)}", registerUrl);
        Assert.Equal(updatedToken, InvitationFlow.EffectiveToken(updatedToken, returnUrl));
        Assert.Equal(InvitationStatus.Valid, (await fixture.Service.InspectAsync(updatedToken)).Status);
    }

    [Fact]
    public async Task GuestAccess_ReturnsOnlyTheInvitedReadOnlyEvent()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        await using (var db = fixture.CreateDbContext())
        {
            db.Events.Add(new CalendarEvent
            {
                OwnerId = fixture.Event.OwnerId,
                Title = "Owner's private event",
                Start = fixture.Event.Start.AddDays(1),
                End = fixture.Event.End.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.GetGuestEventAsync(fixture.Token);

        Assert.Equal(InvitationStatus.Valid, result.Status);
        var sharedEvent = Assert.IsType<GuestEventView>(result.Event);
        Assert.Equal(fixture.Event.Title, sharedEvent.Title);
        Assert.NotEqual("Owner's private event", sharedEvent.Title);
        Assert.Equal("Owner", sharedEvent.OrganizerName);
        Assert.Equal("https://meet.google.com/abc-defg-hij", sharedEvent.MeetingUrl);
    }

    [Fact]
    public void GuestProjection_ContainsNoEditDeleteShareOrCalendarAccessState()
    {
        var propertyNames = typeof(GuestEventView).GetProperties().Select(property => property.Name).ToHashSet();

        Assert.DoesNotContain("Id", propertyNames);
        Assert.DoesNotContain("OwnerId", propertyNames);
        Assert.DoesNotContain("CanEdit", propertyNames);
        Assert.DoesNotContain("CollaboratorEmails", propertyNames);
        Assert.DoesNotContain("Participants", propertyNames);
    }

    [Fact]
    public async Task UnregisteredRecipient_AfterRegistrationCanClaimSharedEvent()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var invitedUser = fixture.NewUser("future@luma.test", "Future User");
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(invitedUser);
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.ClaimAsync(fixture.Token, invitedUser.Id);

        Assert.Equal(InvitationClaimStatus.Success, result.Status);
        Assert.Equal(fixture.Event.Id, result.EventId);
        await using var verification = fixture.CreateDbContext();
        Assert.True(await verification.EventParticipants.AnyAsync(
            participant => participant.EventId == fixture.Event.Id && participant.UserId == invitedUser.Id));
        var invitation = await verification.EventInvitations.SingleAsync();
        Assert.Equal(invitedUser.Id, invitation.ClaimedByUserId);
        Assert.NotNull(invitation.ClaimedUtc);
        Assert.Equal(EventInvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public async Task ExpiredInvitation_IsRejectedWithoutGrantingAccess()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test", expiresUtc: DateTime.UtcNow.AddMinutes(-1));
        var user = fixture.NewUser("future@luma.test", "Future User");
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.ClaimAsync(fixture.Token, user.Id);

        Assert.Equal(InvitationClaimStatus.Expired, result.Status);
        await using var verification = fixture.CreateDbContext();
        Assert.Empty(await verification.EventParticipants.ToListAsync());
    }

    [Fact]
    public async Task ExpiredInvitation_CannotBeOpenedAsGuest()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test", expiresUtc: DateTime.UtcNow.AddMinutes(-1));

        var result = await fixture.Service.GetGuestEventAsync(fixture.Token);

        Assert.Equal(InvitationStatus.Expired, result.Status);
        Assert.Null(result.Event);
    }

    [Fact]
    public async Task InvalidInvitation_IsRejected()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");

        var inspection = await fixture.Service.InspectAsync("not-the-issued-token");

        Assert.Equal(InvitationStatus.Invalid, inspection.Status);
    }

    [Fact]
    public async Task InvalidInvitation_CannotBeOpenedAsGuestOrShowGuestLogin()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");

        var guestResult = await fixture.Service.GetGuestEventAsync("not-the-issued-token");
        var loginContext = await InvitationFlow.ResolveLoginContextAsync(
            fixture.Service, "not-the-issued-token", "/invitation?token=not-the-issued-token");

        Assert.Equal(InvitationStatus.Invalid, guestResult.Status);
        Assert.Null(guestResult.Event);
        Assert.False(loginContext.ShowGuestOption);
    }

    [Fact]
    public void SignInUrl_PreservesInvitationContext()
    {
        const string token = "secure-token-with_symbols";
        var returnUrl = InvitationFlow.InvitationUrl(token);
        var loginUrl = InvitationFlow.LoginUrl(returnUrl, token);

        Assert.StartsWith("/login?", loginUrl);
        Assert.Contains($"returnUrl={Uri.EscapeDataString(returnUrl)}", loginUrl);
        Assert.Contains($"invitationToken={Uri.EscapeDataString(token)}", loginUrl);
        Assert.Equal(token, InvitationFlow.EffectiveToken(null, returnUrl));
    }

    [Fact]
    public void RegistrationUrl_PreservesInvitationContext()
    {
        const string token = "secure-token-with_symbols";
        var returnUrl = InvitationFlow.InvitationUrl(token);
        var registerUrl = InvitationFlow.RegisterUrl(returnUrl, token);

        Assert.StartsWith("/register?", registerUrl);
        Assert.Contains($"returnUrl={Uri.EscapeDataString(returnUrl)}", registerUrl);
        Assert.Contains($"invitationToken={Uri.EscapeDataString(token)}", registerUrl);
        Assert.Equal(token, InvitationFlow.EffectiveToken(token, returnUrl));
    }

    [Fact]
    public async Task Invitation_CannotBeClaimedByDifferentEmail()
    {
        var fixture = await Fixture.CreateAsync("future@luma.test");
        var otherUser = fixture.NewUser("someone-else@luma.test", "Other User");
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(otherUser);
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.ClaimAsync(fixture.Token, otherUser.Id);

        Assert.Equal(InvitationClaimStatus.EmailMismatch, result.Status);
        await using var verification = fixture.CreateDbContext();
        Assert.Empty(await verification.EventParticipants.ToListAsync());
        Assert.Null((await verification.EventInvitations.SingleAsync()).ClaimedUtc);
    }

    private sealed class Fixture(
        DbContextOptions<CalendarDbContext> options,
        CalendarEvent calendarEvent,
        EventInvitation invitation,
        string token,
        IInvitationAccessTokenService accessTokens)
    {
        public CalendarEvent Event { get; } = calendarEvent;
        public EventInvitation Invitation { get; } = invitation;
        public string Token { get; } = token;
        public string UpdatedToken => accessTokens.Create(Invitation.Id);
        public EventInvitationService Service { get; } = new(new TestDbContextFactory(options), accessTokens);

        public static async Task<Fixture> CreateAsync(string recipientEmail, DateTime? expiresUtc = null)
        {
            var options = new DbContextOptionsBuilder<CalendarDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var owner = NewUserCore("owner@luma.test", "Owner");
            var calendarEvent = new CalendarEvent
            {
                OwnerId = owner.Id,
                Title = "Planning session",
                Start = new DateTime(2026, 10, 20, 9, 0, 0),
                End = new DateTime(2026, 10, 20, 10, 0, 0),
                MeetingUrl = "https://meet.google.com/abc-defg-hij"
            };
            var token = "secure-test-token-123";
            var invitation = new EventInvitation
            {
                EventId = calendarEvent.Id,
                RecipientEmail = recipientEmail,
                NormalizedRecipientEmail = recipientEmail.ToUpperInvariant(),
                Status = EventInvitationStatus.Pending,
                TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                CreatedUtc = DateTime.UtcNow.AddMinutes(-5),
                ExpiresUtc = expiresUtc ?? DateTime.UtcNow.AddDays(14)
            };
            await using var db = new CalendarDbContext(options);
            db.Users.Add(owner);
            db.Events.Add(calendarEvent);
            db.EventInvitations.Add(invitation);
            await db.SaveChangesAsync();
            var accessTokens = new InvitationAccessTokenService(new EphemeralDataProtectionProvider());
            return new Fixture(options, calendarEvent, invitation, token, accessTokens);
        }

        public CalendarDbContext CreateDbContext() => new(options);
        public AppUser NewUser(string email, string name) => NewUserCore(email, name);

        private static AppUser NewUserCore(string email, string name) => new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Name = name,
            PasswordHash = "not-used"
        };
    }

    private sealed class TestDbContextFactory(DbContextOptions<CalendarDbContext> options)
        : IDbContextFactory<CalendarDbContext>
    {
        public CalendarDbContext CreateDbContext() => new(options);
    }
}
