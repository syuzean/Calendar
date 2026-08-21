using System.Security.Cryptography;
using System.Text;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calendar.Tests;

public sealed class EventInvitationServiceTests
{
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
        string token)
    {
        public CalendarEvent Event { get; } = calendarEvent;
        public string Token { get; } = token;
        public EventInvitationService Service { get; } = new(new TestDbContextFactory(options));

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
            return new Fixture(options, calendarEvent, token);
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
