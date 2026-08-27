using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public sealed record CreateProjectRequest(string Name, string? Description);

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    string Description,
    string CreatedByName,
    DateTime CreatedAt,
    Guid Version,
    int TaskCount,
    int DoneTaskCount);

public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string Description,
    string CreatedByName,
    DateTime CreatedAt,
    Guid Version,
    int TaskCount,
    int DoneTaskCount);

public sealed class ProjectNotFoundException : Exception
{
    public ProjectNotFoundException() : base("This project no longer exists.") { }
}

public sealed class ProjectStore(
    IDbContextFactory<CalendarDbContext> dbFactory,
    AuthenticationStateProvider authenticationStateProvider)
{
    public async Task<Guid> CreateAsync(CreateProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var creatorId = await GetCurrentUserIdAsync();
        Validate(request);

        await using var db = await dbFactory.CreateDbContextAsync();
        var creatorExists = await db.Users.AsNoTracking().AnyAsync(user => user.Id == creatorId);
        if (!creatorExists)
            throw new UnauthorizedAccessException("The signed-in LUMA user could not be found.");

        var project = new LumaProject
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            CreatedByUserId = creatorId,
            CreatedAt = DateTime.UtcNow,
            Version = Guid.NewGuid()
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    public async Task<IReadOnlyList<ProjectSummary>> LoadAsync(string? search = null)
    {
        await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var projects = db.Projects.AsNoTracking();
        var normalizedSearch = search?.Trim().ToLower();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
            projects = projects.Where(project => project.Name.ToLower().Contains(normalizedSearch));

        return await projects
            .OrderByDescending(project => project.CreatedAt)
            .ThenBy(project => project.Name)
            .Select(project => new ProjectSummary(
                project.Id,
                project.Name,
                project.Description,
                project.CreatedByUser!.Name,
                project.CreatedAt,
                project.Version,
                project.Tasks.Count,
                project.Tasks.Count(task => task.WorkStatus == TaskWorkStatus.Done)))
            .ToListAsync();
    }

    public async Task<ProjectDetails> LoadDetailsAsync(Guid projectId)
    {
        await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Projects.AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => new ProjectDetails(
                project.Id,
                project.Name,
                project.Description,
                project.CreatedByUser!.Name,
                project.CreatedAt,
                project.Version,
                project.Tasks.Count,
                project.Tasks.Count(task => task.WorkStatus == TaskWorkStatus.Done)))
            .SingleOrDefaultAsync()
            ?? throw new ProjectNotFoundException();
    }

    private async Task<Guid> GetCurrentUserIdAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            throw new UnauthorizedAccessException("You must sign in to access projects.");
        }

        return userId;
    }

    private static void Validate(CreateProjectRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Project name is required.");
        else if (request.Name.Trim().Length > 120)
            errors.Add("Project name cannot exceed 120 characters.");

        if ((request.Description?.Trim().Length ?? 0) > 2000)
            errors.Add("Project description cannot exceed 2000 characters.");

        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));
    }
}
