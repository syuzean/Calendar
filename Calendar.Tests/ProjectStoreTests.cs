using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calendar.Tests;

public sealed class ProjectStoreTests
{
    [Fact]
    public async Task AuthenticatedUser_CanCreateProject()
    {
        var fixture = await TestFixture.CreateAsync();

        var id = await fixture.StoreFor(fixture.Creator)
            .CreateAsync(new("Product launch", "Coordinate the launch."));

        Assert.NotEqual(Guid.Empty, id);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(id, (await db.Projects.SingleAsync()).Id);
    }

    [Fact]
    public async Task Creator_ComesFromAuthenticatedIdentity()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.StoreFor(fixture.Creator).CreateAsync(new("Identity project", null));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(fixture.Creator.Id, (await db.Projects.SingleAsync()).CreatedByUserId);
    }

    [Fact]
    public async Task EmptyProjectName_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.StoreFor(fixture.Creator).CreateAsync(new("   ", "Description")));

        Assert.Contains("name is required", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Projects.ToListAsync());
    }

    [Fact]
    public async Task Description_IsOptional()
    {
        var fixture = await TestFixture.CreateAsync();

        await fixture.StoreFor(fixture.Creator).CreateAsync(new("No description", null));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(string.Empty, (await db.Projects.SingleAsync()).Description);
    }

    [Fact]
    public async Task Project_PersistsWithCreatedAtAndVersion()
    {
        var fixture = await TestFixture.CreateAsync();
        var before = DateTime.UtcNow;

        var id = await fixture.StoreFor(fixture.Creator).CreateAsync(new("Persistent project", "Saved"));

        await using var db = fixture.CreateDbContext();
        var project = await db.Projects.SingleAsync(item => item.Id == id);
        Assert.Equal("Persistent project", project.Name);
        Assert.Equal("Saved", project.Description);
        Assert.InRange(project.CreatedAt, before, DateTime.UtcNow);
        Assert.NotEqual(Guid.Empty, project.Version);
    }

    [Fact]
    public async Task AuthenticatedUsers_CanLoadSharedProjects()
    {
        var fixture = await TestFixture.CreateAsync();
        var id = await fixture.StoreFor(fixture.Creator).CreateAsync(new("Shared workspace", null));

        var project = Assert.Single(await fixture.StoreFor(fixture.OtherUser).LoadAsync());

        Assert.Equal(id, project.Id);
        Assert.Equal(fixture.Creator.Name, project.CreatedByName);
    }

    [Fact]
    public async Task ProjectsList_ReturnsCreatedProjectsAndSupportsNameSearch()
    {
        var fixture = await TestFixture.CreateAsync();
        var store = fixture.StoreFor(fixture.Creator);
        await store.CreateAsync(new("Website launch", null));
        await store.CreateAsync(new("Mobile application", null));

        var all = await store.LoadAsync();
        var filtered = await store.LoadAsync("WEBSITE");

        Assert.Equal(2, all.Count);
        Assert.Equal("Website launch", Assert.Single(filtered).Name);
    }

    [Fact]
    public async Task AuthenticatedUser_CanOpenProjectDetails()
    {
        var fixture = await TestFixture.CreateAsync();
        var id = await fixture.StoreFor(fixture.Creator)
            .CreateAsync(new("Open project", "Visible to signed-in users."));

        var details = await fixture.StoreFor(fixture.OtherUser).LoadDetailsAsync(id);

        Assert.Equal("Open project", details.Name);
        Assert.Equal("Visible to signed-in users.", details.Description);
        Assert.Equal(fixture.Creator.Name, details.CreatedByName);
    }

    [Fact]
    public async Task ProjectSummariesAndDetails_ReturnTaskAndDoneCounts()
    {
        var fixture = await TestFixture.CreateAsync();
        var projectId = await fixture.StoreFor(fixture.Creator).CreateAsync(new("Counted", null));
        await using (var db = fixture.CreateDbContext())
        {
            db.Tasks.AddRange(
                TestFixture.NewTask(fixture, projectId, "Open", TaskWorkStatus.InProgress),
                TestFixture.NewTask(fixture, projectId, "Done one", TaskWorkStatus.Done),
                TestFixture.NewTask(fixture, projectId, "Done two", TaskWorkStatus.Done),
                TestFixture.NewTask(fixture, null, "Independent", TaskWorkStatus.Done));
            await db.SaveChangesAsync();
        }

        var summary = Assert.Single(await fixture.StoreFor(fixture.OtherUser).LoadAsync());
        var details = await fixture.StoreFor(fixture.OtherUser).LoadDetailsAsync(projectId);

        Assert.Equal(3, summary.TaskCount);
        Assert.Equal(2, summary.DoneTaskCount);
        Assert.Equal(3, details.TaskCount);
        Assert.Equal(2, details.DoneTaskCount);
    }

    [Fact]
    public async Task UnauthenticatedCreation_IsRejected()
    {
        var fixture = await TestFixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.AnonymousStore().CreateAsync(new("Blocked project", null)));

        Assert.Contains("sign in", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Projects.ToListAsync());
    }

    private sealed class TestFixture(
        DbContextOptions<CalendarDbContext> options,
        AppUser creator,
        AppUser otherUser)
    {
        public AppUser Creator { get; } = creator;
        public AppUser OtherUser { get; } = otherUser;

        public static async Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<CalendarDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var creator = NewUser("creator@luma.test", "Project Creator");
            var otherUser = NewUser("viewer@luma.test", "Project Viewer");
            await using var db = new CalendarDbContext(options);
            db.Users.AddRange(creator, otherUser);
            await db.SaveChangesAsync();
            return new TestFixture(options, creator, otherUser);
        }

        public CalendarDbContext CreateDbContext() => new(options);
        public ProjectStore StoreFor(AppUser user) =>
            new(new TestDbContextFactory(options), new TestAuthenticationStateProvider(user));
        public ProjectStore AnonymousStore() =>
            new(new TestDbContextFactory(options), new AnonymousAuthenticationStateProvider());

        public static LumaTask NewTask(TestFixture fixture, Guid? projectId, string title, TaskWorkStatus status) =>
            new()
            {
                Title = title,
                CreatorId = fixture.Creator.Id,
                AssigneeId = fixture.OtherUser.Id,
                ProjectId = projectId,
                Deadline = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
                CreatedAt = DateTime.UtcNow,
                AssignmentStatus = TaskAssignmentStatus.Accepted,
                WorkStatus = status,
                Version = Guid.NewGuid()
            };

        private static AppUser NewUser(string email, string name) => new()
        {
            Name = name,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "test"
        };
    }

    private sealed class TestDbContextFactory(DbContextOptions<CalendarDbContext> options)
        : IDbContextFactory<CalendarDbContext>
    {
        public CalendarDbContext CreateDbContext() => new(options);
    }

    private sealed class TestAuthenticationStateProvider(AppUser user) : AuthenticationStateProvider
    {
        private readonly AuthenticationState state = new(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email)
        ], "Test")));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(state);
    }

    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
