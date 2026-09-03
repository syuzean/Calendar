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
    int DoneTaskCount,
    bool CanManageFeatures);

public sealed record SaveFeatureRequest(string Name, string? Description);

public sealed record FeatureSummary(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Name,
    string Description,
    int WorkItemCount);

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
        var currentUserId = await GetCurrentUserIdAsync();
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
                project.Tasks.Count(task => task.WorkStatus == TaskWorkStatus.Done),
                project.CreatedByUserId == currentUserId))
            .SingleOrDefaultAsync()
            ?? throw new ProjectNotFoundException();
    }

    public async Task<IReadOnlyList<FeatureSummary>> LoadFeaturesAsync(Guid? projectId = null)
    {
        _ = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var features = db.Features.AsNoTracking();
        if (projectId is not null)
            features = features.Where(feature => feature.ProjectId == projectId.Value);

        return await features
            .OrderBy(feature => feature.Project!.Name)
            .ThenBy(feature => feature.Name)
            .Select(feature => new FeatureSummary(
                feature.Id,
                feature.ProjectId,
                feature.Project!.Name,
                feature.Name,
                feature.Description,
                feature.TaskFeatures.Count))
            .ToListAsync();
    }

    public async Task<Guid> CreateFeatureAsync(Guid projectId, SaveFeatureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentUserId = await GetCurrentUserIdAsync();
        ValidateFeature(request);
        await using var db = await dbFactory.CreateDbContextAsync();
        var project = await db.Projects.SingleOrDefaultAsync(item => item.Id == projectId)
            ?? throw new ProjectNotFoundException();
        EnsureFeatureManager(project, currentUserId);

        var normalizedName = NormalizeFeatureName(request.Name);
        if (await db.Features.AnyAsync(item => item.ProjectId == projectId && item.NormalizedName == normalizedName))
            throw new ValidationException("A feature with this name already exists in the project.");

        var feature = new LumaFeature
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = request.Name.Trim(),
            NormalizedName = normalizedName,
            Description = request.Description?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUserId
        };
        db.Features.Add(feature);
        await SaveFeatureChangesAsync(db);
        return feature.Id;
    }

    public async Task UpdateFeatureAsync(Guid featureId, SaveFeatureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentUserId = await GetCurrentUserIdAsync();
        ValidateFeature(request);
        await using var db = await dbFactory.CreateDbContextAsync();
        var feature = await db.Features.Include(item => item.Project)
            .SingleOrDefaultAsync(item => item.Id == featureId)
            ?? throw new ValidationException("This feature no longer exists.");
        EnsureFeatureManager(feature.Project!, currentUserId);

        var normalizedName = NormalizeFeatureName(request.Name);
        if (await db.Features.AnyAsync(item => item.ProjectId == feature.ProjectId &&
                                              item.Id != featureId && item.NormalizedName == normalizedName))
            throw new ValidationException("A feature with this name already exists in the project.");

        feature.Name = request.Name.Trim();
        feature.NormalizedName = normalizedName;
        feature.Description = request.Description?.Trim() ?? string.Empty;
        await SaveFeatureChangesAsync(db);
    }

    public async Task DeleteFeatureAsync(Guid featureId)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var feature = await db.Features
            .Include(item => item.Project)
            .Include(item => item.TaskFeatures)
            .SingleOrDefaultAsync(item => item.Id == featureId)
            ?? throw new ValidationException("This feature no longer exists.");
        EnsureFeatureManager(feature.Project!, currentUserId);

        var changedAt = DateTime.UtcNow;
        var mutationId = Guid.NewGuid();
        foreach (var relation in feature.TaskFeatures)
        {
            db.TaskChangeLogs.Add(new TaskChangeLog
            {
                Id = Guid.NewGuid(),
                TaskId = relation.TaskId,
                ActorUserId = currentUserId,
                MutationId = mutationId,
                ChangeType = TaskChangeType.FeatureRemoved,
                FieldName = "FeatureId",
                OldValue = feature.Id.ToString("D"),
                CreatedAt = changedAt
            });
        }

        db.TaskFeatures.RemoveRange(feature.TaskFeatures);
        db.Features.Remove(feature);
        await SaveFeatureChangesAsync(db);
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

    private static void ValidateFeature(SaveFeatureRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Feature name is required.");
        else if (request.Name.Trim().Length > 120)
            errors.Add("Feature name cannot exceed 120 characters.");
        if ((request.Description?.Trim().Length ?? 0) > 2000)
            errors.Add("Feature description cannot exceed 2000 characters.");
        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));
    }

    private static void EnsureFeatureManager(LumaProject project, Guid currentUserId)
    {
        if (project.CreatedByUserId != currentUserId)
            throw new UnauthorizedAccessException("Only the Project creator can manage its features.");
    }

    private static string NormalizeFeatureName(string name) => name.Trim().ToUpperInvariant();

    private static async Task SaveFeatureChangesAsync(CalendarDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            throw new ValidationException("A feature with this name already exists in the project.", exception);
        }
    }
}
