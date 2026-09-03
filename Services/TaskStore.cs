using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Calendar.Services.Email;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public sealed record CreateLumaTaskRequest(
    string Title,
    string? Description,
    Guid? AssigneeId,
    DateOnly? Deadline,
    TaskPriority Priority = TaskPriority.None,
    string? AssigneeEmail = null,
    Guid? ProjectId = null,
    IReadOnlyList<TaskAttachmentUpload>? Attachments = null,
    IReadOnlyCollection<Guid>? DescriptionMentionUserIds = null,
    WorkItemType WorkItemType = WorkItemType.Task,
    BugCategory? BugCategory = null,
    BugSeverity? BugSeverity = null,
    BugReproducibility? BugReproducibility = null,
    string? FoundInVersion = null,
    string? BugEnvironment = null,
    BugAdaptiveDetailsInput? BugDetails = null,
    IReadOnlyList<BugReproductionStepInput>? ReproductionSteps = null,
    string? ReproductionMarkdown = null);

public sealed record RequestTaskDeadlineChange(
    DateOnly? ProposedDeadline,
    string? Comment);

public sealed record UpdateLumaTaskContentRequest(
    string Title,
    string? Description,
    Guid Version,
    TaskPriority? Priority = null,
    Guid? ProjectId = null,
    IReadOnlyList<TaskAttachmentUpload>? NewAttachments = null,
    IReadOnlyCollection<Guid>? RemovedAttachmentIds = null,
    IReadOnlyCollection<Guid>? DescriptionMentionUserIds = null,
    BugCategory? BugCategory = null,
    BugSeverity? BugSeverity = null,
    BugReproducibility? BugReproducibility = null,
    string? FoundInVersion = null,
    string? BugEnvironment = null,
    BugAdaptiveDetailsInput? BugDetails = null,
    IReadOnlyList<BugReproductionStepInput>? ReproductionSteps = null,
    string? ReproductionMarkdown = null);

public sealed record TaskAttachmentUpload(
    string FileName,
    string ContentType,
    long Length,
    Func<Stream> OpenReadStream,
    string? InlineToken = null);

public sealed record BugAdaptiveDetailsInput(
    string? ExpectedResult = null,
    string? ObservedResult = null,
    string? ErrorMessage = null,
    string? ErrorDetails = null,
    string? ExpectedDuration = null,
    string? ActualDuration = null,
    int? Attempts = null,
    string? HttpMethod = null,
    string? Endpoint = null,
    int? StatusCode = null,
    string? ApiRequest = null,
    string? ApiResponse = null,
    string? CorrelationId = null,
    string? DataEntity = null,
    string? DataIdentifier = null,
    string? ExpectedValue = null,
    string? ActualValue = null,
    string? LastKnownGoodVersion = null,
    string? FirstBrokenVersion = null,
    string? WorksOn = null,
    string? FailsOn = null,
    string? Logs = null);

public sealed record BugReproductionStepInput(
    Guid? Id,
    string Content,
    string? ObservedResult,
    bool IsPrimaryFailure,
    IReadOnlyList<TaskAttachmentUpload>? NewImages = null,
    IReadOnlyCollection<Guid>? RemovedImageIds = null);

public sealed record TaskAttachmentDetails(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAt)
{
    public string Url => $"/task-attachments/{Id:D}";
}

public sealed record BugReproductionStepDetails(
    Guid Id,
    int Position,
    string Content,
    string ObservedResult,
    bool IsPrimaryFailure,
    IReadOnlyList<TaskAttachmentDetails> Images);

public static class TaskAttachmentRules
{
    public const long MaximumFileSizeBytes = 5 * 1024 * 1024;
    public const long MaximumTotalSizeBytes = 25 * 1024 * 1024;
    public const int MaximumAttachmentCount = 10;
}

public enum TaskDeadlineFilter
{
    All,
    NoDeadline,
    Overdue,
    Today,
    ThisWeek
}

public enum TaskSortOrder
{
    DeadlineNearest,
    PriorityHighest,
    Newest
}

public sealed record TaskListQuery(
    string? Search = null,
    TaskWorkStatus? WorkStatus = null,
    TaskAssignmentStatus? AssignmentStatus = null,
    TaskPriority? Priority = null,
    TaskDeadlineFilter Deadline = TaskDeadlineFilter.All,
    TaskSortOrder Sort = TaskSortOrder.DeadlineNearest,
    Guid? ProjectId = null,
    IReadOnlyCollection<Guid>? AssigneeIds = null,
    bool IncludeUnassigned = false);

public sealed record ChangeTaskWorkStatusRequest(
    TaskWorkStatus WorkStatus,
    Guid Version);

public sealed record TakeLumaTaskRequest(Guid Version);

public sealed record AddTaskCommentRequest(
    string Text,
    IReadOnlyCollection<Guid>? MentionUserIds = null);

public sealed record TaskAssigneeFilterOption(Guid Id, string Name, bool IsCurrentUser);
public sealed record TaskAssigneeFilterSelection(
    IReadOnlyCollection<Guid> UserIds,
    bool IncludeUnassigned);
public sealed record TaskMentionUserOption(Guid Id, string Name, string Email);
public sealed record TaskMentionDetails(Guid UserId, string UserName);

public sealed record LumaTaskCommentDetails(
    Guid Id,
    Guid TaskId,
    Guid AuthorUserId,
    string AuthorName,
    string Text,
    DateTime CreatedAt);

public sealed record AssignedLumaTask(
    Guid Id,
    string Title,
    string CreatorName,
    DateOnly? Deadline,
    TaskAssignmentStatus AssignmentStatus,
    TaskWorkStatus WorkStatus,
    TaskPriority Priority,
    Guid Version,
    bool CanManageWorkStatus);

public sealed record CreatedLumaTask(
    Guid Id,
    string Title,
    string AssigneeName,
    DateOnly? Deadline,
    TaskAssignmentStatus AssignmentStatus,
    TaskWorkStatus WorkStatus,
    TaskPriority Priority,
    Guid Version,
    bool CanManageWorkStatus,
    bool IsInvited);

public sealed record RelatedLumaTask(
    Guid Id,
    string Title,
    string CreatorName,
    string AssigneeName,
    Guid? AssigneeId,
    Guid? ProjectId,
    string ProjectName,
    DateOnly? Deadline,
    TaskAssignmentStatus AssignmentStatus,
    TaskWorkStatus WorkStatus,
    TaskPriority Priority,
    Guid Version,
    bool IsCreatedByCurrentUser,
    bool IsAssignedToCurrentUser,
    bool CanManageWorkStatus,
    bool IsInvited);

public sealed record LumaTaskDetails(
    Guid Id,
    string Title,
    string Description,
    string CreatorName,
    string AssigneeName,
    bool IsInvited,
    Guid? ProjectId,
    string ProjectName,
    DateOnly? Deadline,
    DateTime CreatedAt,
    TaskAssignmentStatus AssignmentStatus,
    TaskWorkStatus WorkStatus,
    TaskPriority Priority,
    DateTime? AcceptedAt,
    DateOnly? RequestedDeadline,
    string DeadlineChangeComment,
    DateTime? DeadlineChangeRequestedAt,
    Guid Version,
    bool CanAccept,
    bool CanReviewDeadlineChange,
    bool CanEdit,
    bool CanManageWorkStatus,
    bool CanComment,
    bool CanTake,
    IReadOnlyList<TaskAttachmentDetails> Attachments,
    IReadOnlyList<TaskMentionDetails> Mentions,
    WorkItemType WorkItemType = WorkItemType.Task,
    BugCategory? BugCategory = null,
    BugSeverity? BugSeverity = null,
    BugReproducibility? BugReproducibility = null,
    string FoundInVersion = "",
    string BugEnvironment = "",
    BugAdaptiveDetailsInput? BugDetails = null,
    IReadOnlyList<BugReproductionStepDetails>? ReproductionSteps = null,
    string ReproductionMarkdown = "");

public sealed class LumaTaskNotFoundException : Exception
{
    public LumaTaskNotFoundException() : base("This task no longer exists.") { }
}

public sealed class TaskStore(
    IDbContextFactory<CalendarDbContext> dbFactory,
    AuthenticationStateProvider authenticationStateProvider,
    ITaskNotifier taskNotifier,
    ITaskLinkBuilder taskLinkBuilder,
    ITaskAttachmentStorage attachmentStorage,
    ILogger<TaskStore> logger)
{
    public string? LastNotice { get; private set; }

    public async Task<Guid> CreateAsync(CreateLumaTaskRequest request)
    {
        LastNotice = null;
        ArgumentNullException.ThrowIfNull(request);
        var creatorId = await GetCurrentUserIdAsync();

        Validate(request);

        await using var db = await dbFactory.CreateDbContextAsync();
        var maker = await db.Users.AsNoTracking()
            .Where(user => user.Id == creatorId)
            .Select(user => new TaskUser(user.Id, user.Name, user.Email))
            .SingleOrDefaultAsync();
        if (maker is null)
            throw new UnauthorizedAccessException("The signed-in LUMA user could not be found.");

        LumaProject? selectedProject = null;
        if (request.ProjectId is { } projectId)
            selectedProject = await db.Projects.SingleOrDefaultAsync(project => project.Id == projectId)
                ?? throw new ValidationException("Choose an existing LUMA project.");

        TaskUser? doer = null;
        string? invitationEmail = null;
        if (request.AssigneeId is { } assigneeId)
        {
            doer = await db.Users.AsNoTracking()
                .Where(user => user.Id == assigneeId)
                .Select(user => new TaskUser(user.Id, user.Name, user.Email))
                .SingleOrDefaultAsync();
            if (doer is null)
                throw new ValidationException("Choose a registered LUMA user or enter a valid email address as the assignee.");
        }
        else if (!string.IsNullOrWhiteSpace(request.AssigneeEmail))
        {
            invitationEmail = request.AssigneeEmail!.Trim();
            var normalizedEmail = NormalizeEmail(invitationEmail);
            doer = await db.Users.AsNoTracking()
                .Where(user => user.NormalizedEmail == normalizedEmail)
                .Select(user => new TaskUser(user.Id, user.Name, user.Email))
                .SingleOrDefaultAsync();
        }

        var resolvedMentions = await ResolveMentionsAsync(
            db,
            request.Description,
            request.DescriptionMentionUserIds);

        var reproductionMarkdown = request.WorkItemType == WorkItemType.Bug
            ? NormalizeReproductionMarkdown(request.ReproductionMarkdown)
            : null;
        var reproductionInputs = request.WorkItemType == WorkItemType.Bug
            ? ReconcileMarkdownSteps(reproductionMarkdown, request.ReproductionSteps, [])
            : [];

        var entity = new LumaTask
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = resolvedMentions.Description,
            CreatorId = creatorId,
            AssigneeId = doer?.Id,
            ProjectId = request.ProjectId,
            Project = selectedProject,
            Deadline = request.Deadline,
            CreatedAt = DateTime.UtcNow,
            AssignmentStatus = TaskAssignmentStatus.Pending,
            WorkStatus = TaskWorkStatus.ToDo,
            Priority = request.Priority,
            WorkItemType = request.WorkItemType,
            BugCategory = request.BugCategory,
            BugSeverity = request.BugSeverity,
            BugReproducibility = request.BugReproducibility,
            FoundInVersion = NormalizeOptional(request.FoundInVersion),
            BugEnvironment = NormalizeOptional(request.BugEnvironment),
            BugDetails = request.WorkItemType == WorkItemType.Bug
                ? ToBugDetailsEntity(request.BugDetails)
                : null,
            AcceptedAt = null,
            Version = Guid.NewGuid()
        };
        if (entity.BugDetails is not null) entity.BugDetails.ReproductionMarkdown = reproductionMarkdown;
        foreach (var mention in resolvedMentions.Mentions)
        {
            mention.TaskId = entity.Id;
            entity.Mentions.Add(mention);
        }
        if (request.WorkItemType == WorkItemType.Bug)
        {
            foreach (var (step, index) in reproductionInputs.Select((step, index) => (step, index)))
            {
                entity.ReproductionSteps.Add(new BugReproductionStep
                {
                    Id = Guid.NewGuid(),
                    TaskId = entity.Id,
                    Position = index,
                    Content = step.Content.Trim(),
                    ObservedResult = NormalizeOptional(step.ObservedResult),
                    IsPrimaryFailure = step.IsPrimaryFailure
                });
            }
        }

        db.Tasks.Add(entity);
        string? invitationToken = null;
        if (doer is null && invitationEmail is not null)
        {
            var token = TaskInvitationToken.Create();
            invitationToken = token.Token;
            entity.Invitation = new TaskInvitation
            {
                Id = Guid.NewGuid(),
                TaskId = entity.Id,
                InviterId = creatorId,
                RecipientEmail = invitationEmail!,
                NormalizedRecipientEmail = NormalizeEmail(invitationEmail!),
                TokenHash = token.Hash,
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(30),
                Status = TaskInvitationStatus.Pending
            };
        }

        QueueInboxItem(
            db,
            entity,
            creatorId,
            doer?.Id,
            InboxActivityType.TaskAssigned,
            $"{maker.Name} assigned “{entity.Title}” to you.");
        QueueMentionInboxItems(
            db,
            entity,
            creatorId,
            $"{maker.Name} mentioned you in “{entity.Title}”.",
            resolvedMentions.Mentions.Select(item => item.UserId));
        var storedAttachments = new List<TaskAttachment>();
        try
        {
            var genericAttachments = await StoreAttachmentsAsync(
                entity.Id, creatorId, request.Attachments, 0, 0);
            storedAttachments.AddRange(genericAttachments);
            entity.Description = TaskMarkdownImageSyntax.ResolvePendingUrls(
                entity.Description, request.Attachments, genericAttachments);
            foreach (var attachment in genericAttachments)
                entity.Attachments.Add(attachment);

            var storedCount = genericAttachments.Count;
            var storedSize = genericAttachments.Sum(item => item.SizeBytes);
            var requestedSteps = reproductionInputs;
            var reproductionUploads = new List<TaskAttachmentUpload>();
            var reproductionStored = new List<TaskAttachment>();
            for (var index = 0; index < entity.ReproductionSteps.Count; index++)
            {
                var step = entity.ReproductionSteps.ElementAt(index);
                var images = await StoreAttachmentsAsync(
                    entity.Id, creatorId, requestedSteps[index].NewImages, storedCount, storedSize, step.Id);
                storedAttachments.AddRange(images);
                storedCount += images.Count;
                storedSize += images.Sum(item => item.SizeBytes);
                foreach (var image in images)
                    entity.Attachments.Add(image);
                step.Content = TaskMarkdownImageSyntax.ResolvePendingUrls(step.Content, requestedSteps[index].NewImages, images);
                reproductionUploads.AddRange(requestedSteps[index].NewImages ?? []);
                reproductionStored.AddRange(images);
            }
            if (entity.BugDetails is not null)
                entity.BugDetails.ReproductionMarkdown = TaskMarkdownImageSyntax.ResolvePendingUrls(
                    entity.BugDetails.ReproductionMarkdown ?? string.Empty, reproductionUploads, reproductionStored);
            await db.SaveChangesAsync();
        }
        catch
        {
            await DeleteStoredAttachmentsAsync(storedAttachments);
            throw;
        }
        if (doer is not null)
            await NotifyCreatedAfterCommitAsync(entity, maker, doer);
        else if (invitationEmail is not null)
            await NotifyInvitedAfterCommitAsync(entity, maker, invitationEmail!, invitationToken!);
        return entity.Id;
    }

    public async Task<IReadOnlyList<TaskAssigneeFilterOption>> LoadAssigneeFilterOptionsAsync()
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .OrderBy(user => user.Name)
            .ThenBy(user => user.Email)
            .Select(user => new TaskAssigneeFilterOption(
                user.Id,
                user.Name,
                user.Id == currentUserId))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<TaskMentionUserOption>> LoadMentionUsersAsync()
    {
        _ = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
            .OrderBy(user => user.Name)
            .ThenBy(user => user.Email)
            .Select(user => new TaskMentionUserOption(user.Id, user.Name, user.Email))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AssignedLumaTask>> LoadAssignedAsync(TaskListQuery? query = null)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var tasks = db.Tasks.AsNoTracking().Where(task => task.AssigneeId == currentUserId);
        tasks = ApplyListFilters(tasks, query ?? new TaskListQuery());
        return await ApplyListSort(tasks, query?.Sort ?? TaskSortOrder.DeadlineNearest)
            .Select(task => new AssignedLumaTask(
                task.Id,
                task.Title,
                task.Creator!.Name,
                task.Deadline,
                task.AssignmentStatus,
                task.WorkStatus,
                task.Priority,
                task.Version,
                true))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<RelatedLumaTask>> LoadRelatedAsync(TaskListQuery? query = null)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        var effectiveQuery = query ?? new TaskListQuery();
        await using var db = await dbFactory.CreateDbContextAsync();
        var tasks = db.Tasks.AsNoTracking();
        tasks = ApplyListFilters(tasks, effectiveQuery);

        return await ApplyListSort(tasks, effectiveQuery.Sort)
            .Select(task => new RelatedLumaTask(
                task.Id,
                task.Title,
                task.Creator!.Name,
                task.Assignee != null
                    ? task.Assignee.Name
                    : task.Invitation != null ? task.Invitation.RecipientEmail : "Unassigned",
                task.AssigneeId,
                task.ProjectId,
                task.Project != null ? task.Project.Name : string.Empty,
                task.Deadline,
                task.AssignmentStatus,
                task.WorkStatus,
                task.Priority,
                task.Version,
                task.CreatorId == currentUserId,
                task.AssigneeId == currentUserId,
                task.AssigneeId == currentUserId,
                task.AssigneeId == null && task.Invitation != null &&
                    task.Invitation.Status == TaskInvitationStatus.Pending))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<RelatedLumaTask>> LoadProjectTasksAsync(
        Guid projectId,
        TaskListQuery? query = null)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        var effectiveQuery = (query ?? new TaskListQuery()) with { ProjectId = projectId };
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Projects.AsNoTracking().AnyAsync(project => project.Id == projectId))
            throw new ProjectNotFoundException();

        var tasks = ApplyListFilters(db.Tasks.AsNoTracking(), effectiveQuery);
        return await ApplyListSort(tasks, effectiveQuery.Sort)
            .Select(task => new RelatedLumaTask(
                task.Id,
                task.Title,
                task.Creator!.Name,
                task.Assignee != null
                    ? task.Assignee.Name
                    : task.Invitation != null ? task.Invitation.RecipientEmail : "Unassigned",
                task.AssigneeId,
                task.ProjectId,
                task.Project != null ? task.Project.Name : string.Empty,
                task.Deadline,
                task.AssignmentStatus,
                task.WorkStatus,
                task.Priority,
                task.Version,
                task.CreatorId == currentUserId,
                task.AssigneeId == currentUserId,
                task.AssigneeId == currentUserId,
                task.AssigneeId == null && task.Invitation != null &&
                    task.Invitation.Status == TaskInvitationStatus.Pending))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<CreatedLumaTask>> LoadCreatedAsync(TaskListQuery? query = null)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var tasks = db.Tasks.AsNoTracking().Where(task => task.CreatorId == currentUserId);
        tasks = ApplyListFilters(tasks, query ?? new TaskListQuery());
        return await ApplyListSort(tasks, query?.Sort ?? TaskSortOrder.DeadlineNearest)
            .Select(task => new CreatedLumaTask(
                task.Id,
                task.Title,
                task.Assignee != null
                    ? task.Assignee.Name
                    : task.Invitation != null ? task.Invitation.RecipientEmail : "Unassigned",
                task.Deadline,
                task.AssignmentStatus,
                task.WorkStatus,
                task.Priority,
                task.Version,
                task.AssigneeId == currentUserId,
                task.AssigneeId == null && task.Invitation != null &&
                    task.Invitation.Status == TaskInvitationStatus.Pending))
            .ToListAsync();
    }

    public async Task<LumaTaskDetails> LoadDetailsAsync(Guid taskId)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.AsNoTracking()
            .Where(item => item.Id == taskId)
            .Select(item => new
            {
                item.CreatorId,
                item.AssigneeId,
                item.Id,
                item.Title,
                item.Description,
                CreatorName = item.Creator!.Name,
                AssigneeName = item.Assignee != null
                    ? item.Assignee.Name
                    : item.Invitation != null ? item.Invitation.RecipientEmail : "Unassigned",
                HasInvitation = item.Invitation != null,
                IsInvited = item.AssigneeId == null && item.Invitation != null &&
                    item.Invitation.Status == TaskInvitationStatus.Pending,
                item.ProjectId,
                ProjectName = item.Project != null ? item.Project.Name : string.Empty,
                item.Deadline,
                item.CreatedAt,
                item.AssignmentStatus,
                item.WorkStatus,
                item.Priority,
                item.WorkItemType,
                item.BugCategory,
                item.BugSeverity,
                item.BugReproducibility,
                item.FoundInVersion,
                item.BugEnvironment,
                BugDetails = item.BugDetails == null ? null : new BugAdaptiveDetailsInput(
                    item.BugDetails.ExpectedResult,
                    item.BugDetails.ObservedResult,
                    item.BugDetails.ErrorMessage,
                    item.BugDetails.ErrorDetails,
                    item.BugDetails.ExpectedDuration,
                    item.BugDetails.ActualDuration,
                    item.BugDetails.Attempts,
                    item.BugDetails.HttpMethod,
                    item.BugDetails.Endpoint,
                    item.BugDetails.StatusCode,
                    item.BugDetails.ApiRequest,
                    item.BugDetails.ApiResponse,
                    item.BugDetails.CorrelationId,
                    item.BugDetails.DataEntity,
                    item.BugDetails.DataIdentifier,
                    item.BugDetails.ExpectedValue,
                    item.BugDetails.ActualValue,
                    item.BugDetails.LastKnownGoodVersion,
                    item.BugDetails.FirstBrokenVersion,
                    item.BugDetails.WorksOn,
                    item.BugDetails.FailsOn,
                    item.BugDetails.Logs),
                ReproductionMarkdown = item.BugDetails == null ? null : item.BugDetails.ReproductionMarkdown,
                ReproductionSteps = item.ReproductionSteps
                    .OrderBy(step => step.Position)
                    .Select(step => new BugReproductionStepDetails(
                        step.Id,
                        step.Position,
                        step.Content,
                        step.ObservedResult ?? string.Empty,
                        step.IsPrimaryFailure,
                        step.Attachments.OrderBy(image => image.CreatedAt)
                            .Select(image => new TaskAttachmentDetails(
                                image.Id, image.OriginalFileName, image.ContentType, image.SizeBytes, image.CreatedAt))
                            .ToList()))
                    .ToList(),
                item.AcceptedAt,
                item.RequestedDeadline,
                item.DeadlineChangeComment,
                item.DeadlineChangeRequestedAt,
                item.Version,
                Mentions = item.Mentions
                    .Select(mention => new TaskMentionDetails(
                        mention.UserId,
                        mention.User!.Name))
                    .ToList(),
                Attachments = item.Attachments
                    .Where(attachment => attachment.BugReproductionStepId == null)
                    .OrderBy(attachment => attachment.CreatedAt)
                    .Select(attachment => new TaskAttachmentDetails(
                        attachment.Id,
                        attachment.OriginalFileName,
                        attachment.ContentType,
                        attachment.SizeBytes,
                        attachment.CreatedAt))
                    .ToList()
            })
            .SingleOrDefaultAsync();

        if (task is null) throw new LumaTaskNotFoundException();

        return new LumaTaskDetails(
            task.Id,
            task.Title,
            TaskMentionSyntax.Canonicalize(task.Description, task.Mentions.ToDictionary(item => item.UserId, item => item.UserName)),
            task.CreatorName,
            task.AssigneeName,
            task.IsInvited,
            task.ProjectId,
            task.ProjectName,
            task.Deadline,
            task.CreatedAt,
            task.AssignmentStatus,
            task.WorkStatus,
            task.Priority,
            task.AcceptedAt,
            task.RequestedDeadline,
            task.DeadlineChangeComment ?? string.Empty,
            task.DeadlineChangeRequestedAt,
            task.Version,
            task.AssigneeId == currentUserId,
            task.CreatorId == currentUserId,
            task.CreatorId == currentUserId,
            task.AssigneeId == currentUserId,
            task.CreatorId == currentUserId || task.AssigneeId == currentUserId,
            task.AssigneeId is null && !task.HasInvitation,
            task.Attachments,
            task.Mentions,
            task.WorkItemType,
            task.BugCategory,
            task.BugSeverity,
            task.BugReproducibility,
            task.FoundInVersion ?? string.Empty,
            task.BugEnvironment ?? string.Empty,
            task.BugDetails,
            task.ReproductionSteps,
            string.IsNullOrWhiteSpace(task.ReproductionMarkdown)
                ? BugReproductionMarkdown.FromLegacySteps(task.ReproductionSteps)
                : task.ReproductionMarkdown);
    }

    public async Task<LumaTaskDetails> AcceptAsync(Guid taskId)
    {
        LastNotice = null;
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.Tasks
            .Include(item => item.Creator)
            .Include(item => item.Assignee)
            .Include(item => item.Project)
            .Include(item => item.Attachments)
            .Include(item => item.Mentions)
                .ThenInclude(mention => mention.User)
            .SingleOrDefaultAsync(item => item.Id == taskId)
            ?? throw new LumaTaskNotFoundException();

        if (task.AssigneeId != currentUserId)
            throw new UnauthorizedAccessException("Only the assigned user can accept this task.");

        if (task.AssignmentStatus == TaskAssignmentStatus.Accepted)
            return ToDetails(task, currentUserId);
        if (task.AssignmentStatus == TaskAssignmentStatus.DeadlineChangeRequested)
            throw new ValidationException("The Task Maker must review the active deadline-change request before this task can be accepted.");

        task.AssignmentStatus = TaskAssignmentStatus.Accepted;
        task.AcceptedAt = DateTime.UtcNow;
        task.Version = Guid.NewGuid();
        QueueInboxItem(
            db,
            task,
            currentUserId,
            task.CreatorId,
            InboxActivityType.TaskAccepted,
            $"{task.Assignee!.Name} accepted “{task.Title}”.");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await db.Entry(task).ReloadAsync();
            if (task.AssigneeId != currentUserId)
                throw new UnauthorizedAccessException("Only the assigned user can accept this task.");
            if (task.AssignmentStatus != TaskAssignmentStatus.Accepted)
                throw new ValidationException("The task changed before it could be accepted. Reopen it and try again.", exception);
        }

        return ToDetails(task, currentUserId);
    }

    public async Task<LumaTaskDetails> TakeAsync(Guid taskId, TakeLumaTaskRequest request)
    {
        LastNotice = null;
        ArgumentNullException.ThrowIfNull(request);
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await LoadTaskForActionAsync(db, taskId);

        if (task.AssigneeId is not null || task.Invitation is not null)
            throw new ValidationException("This task is already assigned.");
        EnsureCurrentVersion(task, request.Version);

        var doer = await db.Users.SingleOrDefaultAsync(user => user.Id == currentUserId)
            ?? throw new UnauthorizedAccessException("The signed-in LUMA user could not be found.");
        task.AssigneeId = currentUserId;
        task.Assignee = doer;
        task.AssignmentStatus = TaskAssignmentStatus.Accepted;
        task.AcceptedAt = DateTime.UtcNow;
        task.Version = Guid.NewGuid();

        QueueInboxItem(
            db,
            task,
            currentUserId,
            task.CreatorId,
            InboxActivityType.TaskTaken,
            $"{doer.Name} took “{task.Title}”.");
        await SaveActionAsync(db, "The task changed before you could take it. Reopen it and try again.");
        return ToDetails(task, currentUserId);
    }

    public async Task<LumaTaskDetails> RequestDeadlineChangeAsync(Guid taskId, RequestTaskDeadlineChange request)
    {
        LastNotice = null;
        ArgumentNullException.ThrowIfNull(request);
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await LoadTaskForActionAsync(db, taskId);

        if (task.AssigneeId != currentUserId)
            throw new UnauthorizedAccessException("Only the assigned user can request a deadline change.");
        if (task.AssignmentStatus == TaskAssignmentStatus.DeadlineChangeRequested)
            throw new ValidationException("A deadline-change request is already awaiting review.");
        if (task.AssignmentStatus != TaskAssignmentStatus.Pending)
            throw new ValidationException("A deadline change can be requested only while the assignment is pending.");

        if (task.Deadline is null)
            throw new ValidationException("A task without a deadline cannot request a deadline change.");
        ValidateDeadlineChangeRequest(task.Deadline.Value, request);
        task.RequestedDeadline = request.ProposedDeadline!.Value;
        task.DeadlineChangeComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        task.DeadlineChangeRequestedAt = DateTime.UtcNow;
        task.AssignmentStatus = TaskAssignmentStatus.DeadlineChangeRequested;
        task.AcceptedAt = null;
        task.Version = Guid.NewGuid();

        QueueInboxItem(
            db,
            task,
            currentUserId,
            task.CreatorId,
            InboxActivityType.DeadlineChangeRequested,
            $"{task.Assignee!.Name} requested a deadline change for “{task.Title}”.");
        await SaveActionAsync(db, "The task changed before the deadline request could be saved. Reopen it and try again.");
        await NotifyDeadlineChangeRequestedAfterCommitAsync(task);
        return ToDetails(task, currentUserId);
    }

    public async Task<LumaTaskDetails> ApproveDeadlineChangeAsync(Guid taskId)
    {
        LastNotice = null;
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await LoadTaskForActionAsync(db, taskId);

        if (task.CreatorId != currentUserId)
            throw new UnauthorizedAccessException("Only the Task Maker can approve a deadline change.");
        var request = ActiveDeadlineRequest(task);

        task.Deadline = request.RequestedDeadline;
        task.AssignmentStatus = TaskAssignmentStatus.Accepted;
        task.AcceptedAt = DateTime.UtcNow;
        ClearDeadlineRequest(task);
        task.Version = Guid.NewGuid();

        QueueInboxItem(
            db,
            task,
            currentUserId,
            task.AssigneeId,
            InboxActivityType.DeadlineChangeApproved,
            $"{task.Creator!.Name} approved the deadline change for “{task.Title}”.");
        await SaveActionAsync(db, "The task changed before the deadline request could be approved. Reopen it and try again.");
        await NotifyDeadlineChangeApprovedAfterCommitAsync(task, request);
        return ToDetails(task, currentUserId);
    }

    public async Task<LumaTaskDetails> DeclineDeadlineChangeAsync(Guid taskId)
    {
        LastNotice = null;
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await LoadTaskForActionAsync(db, taskId);

        if (task.CreatorId != currentUserId)
            throw new UnauthorizedAccessException("Only the Task Maker can decline a deadline change.");
        var request = ActiveDeadlineRequest(task);

        task.AssignmentStatus = TaskAssignmentStatus.Pending;
        task.AcceptedAt = null;
        ClearDeadlineRequest(task);
        task.Version = Guid.NewGuid();

        QueueInboxItem(
            db,
            task,
            currentUserId,
            task.AssigneeId,
            InboxActivityType.DeadlineChangeDeclined,
            $"{task.Creator!.Name} declined the deadline change for “{task.Title}”.");
        await SaveActionAsync(db, "The task changed before the deadline request could be declined. Reopen it and try again.");
        await NotifyDeadlineChangeDeclinedAfterCommitAsync(task, request);
        return ToDetails(task, currentUserId);
    }

    public async Task<LumaTaskDetails> UpdateContentAsync(Guid taskId, UpdateLumaTaskContentRequest request)
    {
        LastNotice = null;
        ArgumentNullException.ThrowIfNull(request);
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await LoadTaskForActionAsync(db, taskId);

        if (task.CreatorId != currentUserId)
            throw new UnauthorizedAccessException("Only the Task Maker can edit this task.");
        if (task.WorkItemType != WorkItemType.Bug && request.ReproductionMarkdown is not null)
            throw new ValidationException("Reproduction steps can only be saved for Bug work items.");

        ValidateContentUpdate(request);
        var bugErrors = new List<string>();
        ValidateBugMetadata(
            bugErrors,
            task.WorkItemType,
            request.BugCategory,
            request.BugSeverity,
            request.BugReproducibility,
            request.FoundInVersion,
            request.BugEnvironment);
        var effectiveBugDetails = task.WorkItemType == WorkItemType.Bug
            ? request.BugDetails ?? ToBugDetailsInput(task.BugDetails)
            : null;
        var effectiveReproductionMarkdown = task.WorkItemType == WorkItemType.Bug
            ? NormalizeReproductionMarkdown(request.ReproductionMarkdown ?? task.BugDetails?.ReproductionMarkdown ??
                BugReproductionMarkdown.FromLegacySteps(task.ReproductionSteps
                    .OrderBy(step => step.Position)
                    .Select(ToReproductionStepDetails)))
            : null;
        var effectiveReproductionSteps = task.WorkItemType == WorkItemType.Bug
            ? ReconcileMarkdownSteps(effectiveReproductionMarkdown, request.ReproductionSteps,
                task.ReproductionSteps.OrderBy(step => step.Position).Select(ToReproductionStepDetails).ToArray())
            : null;
        ValidateAdaptiveBugDetails(
            bugErrors, task.WorkItemType, effectiveBugDetails, effectiveReproductionSteps, isCreate: false);
        if (bugErrors.Count > 0)
            throw new ValidationException(string.Join(" ", bugErrors));
        EnsureCurrentVersion(task, request.Version);

        var title = request.Title.Trim();
        var resolvedMentions = await ResolveMentionsAsync(
            db,
            request.Description,
            request.DescriptionMentionUserIds ?? task.Mentions
                .Select(mention => mention.UserId)
                .ToArray());
        var description = resolvedMentions.Description;
        var priority = request.Priority ?? task.Priority;
        var bugCategory = task.WorkItemType == WorkItemType.Bug ? request.BugCategory : null;
        var bugSeverity = task.WorkItemType == WorkItemType.Bug ? request.BugSeverity : null;
        var bugReproducibility = task.WorkItemType == WorkItemType.Bug ? request.BugReproducibility : null;
        var foundInVersion = task.WorkItemType == WorkItemType.Bug ? NormalizeOptional(request.FoundInVersion) : null;
        var bugEnvironment = task.WorkItemType == WorkItemType.Bug ? NormalizeOptional(request.BugEnvironment) : null;
        LumaProject? updatedProject = null;
        if (request.ProjectId is { } projectId)
        {
            updatedProject = await db.Projects.SingleOrDefaultAsync(project => project.Id == projectId)
                ?? throw new ValidationException("Choose an existing LUMA project.");
        }
        var titleChanged = !string.Equals(task.Title, title, StringComparison.Ordinal);
        var descriptionChanged = !string.Equals(task.Description, description, StringComparison.Ordinal);
        var priorityChanged = task.Priority != priority;
        var projectChanged = task.ProjectId != request.ProjectId;
        var bugMetadataChanged = task.BugCategory != bugCategory ||
                                 task.BugSeverity != bugSeverity ||
                                 task.BugReproducibility != bugReproducibility ||
                                 !string.Equals(task.FoundInVersion, foundInVersion, StringComparison.Ordinal) ||
                                 !string.Equals(task.BugEnvironment, bugEnvironment, StringComparison.Ordinal);
        var normalizedBugDetails = task.WorkItemType == WorkItemType.Bug
            ? ToBugDetailsInput(ToBugDetailsEntity(effectiveBugDetails))
            : null;
        var adaptiveBugDetailsChanged = !Equals(ToBugDetailsInput(task.BugDetails), normalizedBugDetails);
        var reproductionStepsChanged = request.ReproductionSteps is not null &&
                                       !ReproductionStepsEqual(task.ReproductionSteps, effectiveReproductionSteps ?? []);
        var reproductionMarkdownChanged = request.ReproductionMarkdown is not null &&
            !string.Equals(task.BugDetails?.ReproductionMarkdown, effectiveReproductionMarkdown, StringComparison.Ordinal);
        var existingMentionKeys = task.Mentions
            .Select(mention => mention.UserId)
            .ToHashSet();
        var updatedMentionKeys = resolvedMentions.Mentions
            .Select(mention => mention.UserId)
            .ToHashSet();
        var mentionsChanged = !existingMentionKeys.SetEquals(updatedMentionKeys);
        var existingMentionedUserIds = task.Mentions.Select(mention => mention.UserId).ToHashSet();
        var newlyMentionedUserIds = resolvedMentions.Mentions
            .Select(mention => mention.UserId)
            .Distinct()
            .Where(userId => !existingMentionedUserIds.Contains(userId))
            .ToArray();
        var reproductionWasSubmitted = request.ReproductionSteps is not null || request.ReproductionMarkdown is not null;
        var submittedStepIds = (effectiveReproductionSteps ?? [])
            .Where(step => step.Id is not null)
            .Select(step => step.Id!.Value)
            .ToHashSet();
        if (reproductionWasSubmitted && submittedStepIds.Any(id => task.ReproductionSteps.All(step => step.Id != id)))
            throw new ValidationException("One or more reproduction steps no longer belong to this bug.");
        var removedSteps = !reproductionWasSubmitted
            ? []
            : task.ReproductionSteps.Where(step => !submittedStepIds.Contains(step.Id)).ToArray();
        var removedIds = (request.RemovedAttachmentIds ?? [])
            .Concat((effectiveReproductionSteps ?? []).SelectMany(step => step.RemovedImageIds ?? []))
            .Concat(removedSteps.SelectMany(step => step.Attachments).Select(attachment => attachment.Id))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var removedAttachments = task.Attachments
            .Where(attachment => removedIds.Contains(attachment.Id))
            .ToArray();
        if (removedAttachments.Length != removedIds.Length)
            throw new ValidationException("One or more selected attachments no longer belong to this task.");

        var remainingCount = task.Attachments.Count - removedAttachments.Length;
        var remainingSize = task.Attachments.Sum(attachment => attachment.SizeBytes) -
                            removedAttachments.Sum(attachment => attachment.SizeBytes);
        var storedAttachments = await StoreAttachmentsAsync(
            task.Id, currentUserId, request.NewAttachments, remainingCount, remainingSize);
        var allStoredAttachments = new List<TaskAttachment>(storedAttachments);
        description = TaskMarkdownImageSyntax.ResolvePendingUrls(
            description, request.NewAttachments, storedAttachments);
        descriptionChanged = !string.Equals(task.Description, description, StringComparison.Ordinal);
        var stepInputsWithImages = (effectiveReproductionSteps ?? [])
            .Where(step => (step.NewImages?.Count ?? 0) > 0)
            .ToArray();
        var attachmentsChanged = removedAttachments.Length > 0 || storedAttachments.Count > 0 || stepInputsWithImages.Length > 0;
        if (!titleChanged && !descriptionChanged && !priorityChanged && !projectChanged && !attachmentsChanged && !mentionsChanged &&
            !bugMetadataChanged && !adaptiveBugDetailsChanged && !reproductionStepsChanged && !reproductionMarkdownChanged)
            return ToDetails(task, currentUserId);

        task.Title = title;
        task.Description = description;
        task.Priority = priority;
        task.ProjectId = request.ProjectId;
        task.Project = updatedProject;
        task.BugCategory = bugCategory;
        task.BugSeverity = bugSeverity;
        task.BugReproducibility = bugReproducibility;
        task.FoundInVersion = foundInVersion;
        task.BugEnvironment = bugEnvironment;
        if (task.WorkItemType == WorkItemType.Bug)
        {
            task.BugDetails ??= new LumaTaskBugDetails { TaskId = task.Id };
            ApplyBugDetails(task.BugDetails, effectiveBugDetails);
            task.BugDetails.ReproductionMarkdown = effectiveReproductionMarkdown;
        }
        task.Version = Guid.NewGuid();
        db.TaskAttachments.RemoveRange(removedAttachments);
        foreach (var attachment in removedAttachments)
        {
            task.Attachments.Remove(attachment);
            attachment.BugReproductionStep?.Attachments.Remove(attachment);
        }
        db.BugReproductionSteps.RemoveRange(removedSteps);
        foreach (var step in removedSteps)
            task.ReproductionSteps.Remove(step);
        try
        {
            if (request.ReproductionSteps is not null || request.ReproductionMarkdown is not null)
            {
                var countAfterRemoval = remainingCount + storedAttachments.Count;
                var sizeAfterRemoval = remainingSize + storedAttachments.Sum(item => item.SizeBytes);
                var reproductionUploads = new List<TaskAttachmentUpload>();
                var reproductionStored = new List<TaskAttachment>();
                for (var index = 0; index < (effectiveReproductionSteps?.Count ?? 0); index++)
                {
                    var input = effectiveReproductionSteps![index];
                    var step = input.Id is { } stepId
                        ? task.ReproductionSteps.Single(item => item.Id == stepId)
                        : new BugReproductionStep { Id = Guid.NewGuid(), TaskId = task.Id };
                    if (input.Id is null) task.ReproductionSteps.Add(step);
                    step.Position = index;
                    step.Content = input.Content.Trim();
                    step.ObservedResult = NormalizeOptional(input.ObservedResult);
                    step.IsPrimaryFailure = input.IsPrimaryFailure;
                    var stepImages = await StoreAttachmentsAsync(
                        task.Id, currentUserId, input.NewImages, countAfterRemoval, sizeAfterRemoval, step.Id);
                    allStoredAttachments.AddRange(stepImages);
                    countAfterRemoval += stepImages.Count;
                    sizeAfterRemoval += stepImages.Sum(item => item.SizeBytes);
                    foreach (var image in stepImages) task.Attachments.Add(image);
                    step.Content = TaskMarkdownImageSyntax.ResolvePendingUrls(step.Content, input.NewImages, stepImages);
                    reproductionUploads.AddRange(input.NewImages ?? []);
                    reproductionStored.AddRange(stepImages);
                }
                if (task.BugDetails is not null)
                    task.BugDetails.ReproductionMarkdown = TaskMarkdownImageSyntax.ResolvePendingUrls(
                        task.BugDetails.ReproductionMarkdown ?? string.Empty, reproductionUploads, reproductionStored);
            }
        }
        catch
        {
            await DeleteStoredAttachmentsAsync(allStoredAttachments);
            throw;
        }
        db.TaskAttachments.AddRange(allStoredAttachments);
        if (mentionsChanged)
        {
            var removedMentions = task.Mentions
                .Where(mention => !updatedMentionKeys.Contains(mention.UserId))
                .ToArray();
            db.TaskMentions.RemoveRange(removedMentions);
            foreach (var mention in removedMentions)
                task.Mentions.Remove(mention);

            foreach (var mention in resolvedMentions.Mentions
                         .Where(mention => !existingMentionKeys.Contains(mention.UserId)))
            {
                mention.TaskId = task.Id;
                task.Mentions.Add(mention);
            }
        }

        QueueInboxItem(
            db,
            task,
            currentUserId,
            task.AssigneeId,
            InboxActivityType.TaskUpdated,
            $"{task.Creator!.Name} updated “{task.Title}”.");
        QueueMentionInboxItems(
            db,
            task,
            currentUserId,
            $"{task.Creator!.Name} mentioned you in “{task.Title}”.",
            newlyMentionedUserIds);
        try
        {
            await SaveActionAsync(db, "The task changed before your edits could be saved. Reopen it and try again.");
        }
        catch
        {
            await DeleteStoredAttachmentsAsync(allStoredAttachments);
            throw;
        }

        foreach (var attachment in removedAttachments)
        {
            try
            {
                await attachmentStorage.DeleteAsync(attachment.StorageKey);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Removed task attachment file {StorageKey} could not be deleted.", attachment.StorageKey);
            }
        }
        return ToDetails(task, currentUserId);
    }

    public async Task<LumaTaskDetails> ChangeWorkStatusAsync(Guid taskId, ChangeTaskWorkStatusRequest request)
    {
        LastNotice = null;
        ArgumentNullException.ThrowIfNull(request);
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await LoadTaskForActionAsync(db, taskId);

        if (task.AssigneeId != currentUserId)
            throw new UnauthorizedAccessException("Only the Task Doer can update work progress.");
        EnsureCurrentVersion(task, request.Version);
        if (task.AssignmentStatus != TaskAssignmentStatus.Accepted)
            throw new ValidationException("The task must be accepted before work progress can change.");
        if (task.WorkStatus == request.WorkStatus)
            return ToDetails(task, currentUserId);
        if (!IsAllowedWorkStatusTransition(task.WorkStatus, request.WorkStatus))
            throw new ValidationException("That work-status transition is not available.");

        task.WorkStatus = request.WorkStatus;
        task.Version = Guid.NewGuid();

        QueueInboxItem(
            db,
            task,
            currentUserId,
            task.CreatorId,
            InboxActivityType.WorkStatusChanged,
            $"{task.Assignee!.Name} moved “{task.Title}” to {WorkStatusName(task.WorkStatus)}.");
        await SaveActionAsync(db, "The task changed before its progress could be updated. Reopen it and try again.");
        return ToDetails(task, currentUserId);
    }

    public async Task<IReadOnlyList<LumaTaskCommentDetails>> LoadCommentsAsync(Guid taskId)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var access = await db.Tasks.AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => new { task.CreatorId, task.AssigneeId })
            .SingleOrDefaultAsync()
            ?? throw new LumaTaskNotFoundException();

        EnsureTaskAccess(access.CreatorId, access.AssigneeId, currentUserId);
        return await db.TaskComments.AsNoTracking()
            .Where(comment => comment.TaskId == taskId)
            .OrderBy(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .Select(comment => new LumaTaskCommentDetails(
                comment.Id,
                comment.TaskId,
                comment.AuthorUserId,
                comment.Author!.Name,
                comment.Text,
                comment.CreatedAt))
            .ToListAsync();
    }

    public async Task<LumaTaskCommentDetails> AddCommentAsync(Guid taskId, AddTaskCommentRequest request)
    {
        LastNotice = null;
        ArgumentNullException.ThrowIfNull(request);
        var currentUserId = await GetCurrentUserIdAsync();
        var text = ValidateComment(request.Text);

        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await LoadTaskForActionAsync(db, taskId);
        EnsureTaskAccess(task.CreatorId, task.AssigneeId, currentUserId);
        var author = currentUserId == task.CreatorId ? task.Creator! : task.Assignee!;
        var resolvedMentions = await ResolveVisibleMentionsAsync(
            db,
            text,
            request.MentionUserIds,
            2000,
            "Task comments cannot exceed 2000 characters.");
        var comment = new LumaTaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            AuthorUserId = currentUserId,
            Text = resolvedMentions.Text,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var userId in resolvedMentions.UserIds)
        {
            comment.Mentions.Add(new TaskCommentMention
            {
                CommentId = comment.Id,
                UserId = userId,
                CreatedAt = comment.CreatedAt
            });
        }

        db.TaskComments.Add(comment);
        var otherPartyId = currentUserId == task.CreatorId ? task.AssigneeId : task.CreatorId;
        if (otherPartyId is null || !resolvedMentions.UserIds.Contains(otherPartyId.Value))
        {
            QueueInboxItem(
                db,
                task,
                currentUserId,
                otherPartyId,
                InboxActivityType.CommentAdded,
                $"{author.Name} commented on “{task.Title}”.");
        }
        QueueMentionInboxItems(
            db,
            task,
            currentUserId,
            $"{author.Name} mentioned you in a comment on “{task.Title}” — {PreviewComment(resolvedMentions.Text)}",
            resolvedMentions.UserIds);
        await db.SaveChangesAsync();
        return new LumaTaskCommentDetails(
            comment.Id,
            comment.TaskId,
            comment.AuthorUserId,
            author.Name,
            resolvedMentions.Text,
            comment.CreatedAt);
    }

    private async Task NotifyCreatedAfterCommitAsync(LumaTask task, TaskUser maker, TaskUser doer)
    {
        var recipients = DoerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyCreatedAsync(new TaskCreatedNotification(
                task.Title,
                StructuredTaskSummary(task),
                maker.Name,
                doer.Name,
                task.Deadline,
                task.Priority,
                taskLinkBuilder.Task(task.Id),
                recipients,
                task.Project?.Name ?? string.Empty));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task-created notifications could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The task was created, but one or more notification emails could not be sent.";
        }
    }

    private async Task NotifyInvitedAfterCommitAsync(
        LumaTask task,
        TaskUser maker,
        string recipientEmail,
        string invitationToken)
    {
        try
        {
            await taskNotifier.NotifyCreatedAsync(new TaskCreatedNotification(
                task.Title,
                StructuredTaskSummary(task),
                maker.Name,
                recipientEmail,
                task.Deadline,
                task.Priority,
                taskLinkBuilder.Invitation(invitationToken),
                [new(recipientEmail, recipientEmail, TaskNotificationRole.Doer)],
                task.Project?.Name ?? string.Empty));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task-invitation email could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The task and invitation were created, but the invitation email could not be sent.";
        }
    }

    private async Task NotifyDeadlineChangeRequestedAfterCommitAsync(LumaTask task)
    {
        if (task.Assignee is null) return;
        var maker = User(task.Creator!);
        var doer = User(task.Assignee!);
        var recipients = MakerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyDeadlineChangeRequestedAsync(new TaskDeadlineChangeRequestedNotification(
                task.Title,
                maker.Name,
                doer.Name,
                task.Deadline!.Value,
                task.RequestedDeadline!.Value,
                task.DeadlineChangeComment ?? string.Empty,
                task.DeadlineChangeRequestedAt!.Value,
                taskLinkBuilder.Task(task.Id),
                recipients,
                task.Project?.Name ?? string.Empty));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deadline-change request notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The deadline change was requested, but its notification email could not be sent.";
        }
    }

    private async Task NotifyDeadlineChangeApprovedAfterCommitAsync(LumaTask task, DeadlineRequestSnapshot request)
    {
        if (task.Assignee is null) return;
        var maker = User(task.Creator!);
        var doer = User(task.Assignee!);
        var recipients = DoerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyDeadlineChangeApprovedAsync(new TaskDeadlineChangeApprovedNotification(
                task.Title,
                maker.Name,
                doer.Name,
                request.CurrentDeadline,
                request.RequestedDeadline,
                request.Comment,
                task.AcceptedAt!.Value,
                taskLinkBuilder.Task(task.Id),
                recipients,
                task.Project?.Name ?? string.Empty));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deadline-change approval notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The deadline change was approved, but its notification email could not be sent.";
        }
    }

    private async Task NotifyDeadlineChangeDeclinedAfterCommitAsync(LumaTask task, DeadlineRequestSnapshot request)
    {
        if (task.Assignee is null) return;
        var maker = User(task.Creator!);
        var doer = User(task.Assignee!);
        var recipients = DoerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyDeadlineChangeDeclinedAsync(new TaskDeadlineChangeDeclinedNotification(
                task.Title,
                maker.Name,
                doer.Name,
                request.CurrentDeadline,
                request.RequestedDeadline,
                request.Comment,
                DateTime.UtcNow,
                taskLinkBuilder.Task(task.Id),
                recipients,
                task.Project?.Name ?? string.Empty));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deadline-change decline notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The deadline change was declined, but its notification email could not be sent.";
        }
    }

    private static IReadOnlyList<TaskNotificationRecipient> DoerRecipients(TaskUser maker, TaskUser doer) =>
        maker.Id == doer.Id
            ? []
            : [new(doer.Name, doer.Email, TaskNotificationRole.Doer)];

    private static IReadOnlyList<TaskNotificationRecipient> MakerRecipients(TaskUser maker, TaskUser doer) =>
        maker.Id == doer.Id
            ? []
            : [new(maker.Name, maker.Email, TaskNotificationRole.Maker)];

    private static TaskUser User(AppUser user) => new(user.Id, user.Name, user.Email);

    private static string StructuredTaskSummary(LumaTask task)
        => task.Description;

    private async Task<IReadOnlyList<TaskAttachment>> StoreAttachmentsAsync(
        Guid taskId,
        Guid uploaderUserId,
        IReadOnlyList<TaskAttachmentUpload>? uploads,
        int existingCount,
        long existingSizeBytes,
        Guid? bugReproductionStepId = null)
    {
        if (uploads is null || uploads.Count == 0) return [];
        if (existingCount + uploads.Count > TaskAttachmentRules.MaximumAttachmentCount)
            throw new ValidationException($"A task can have up to {TaskAttachmentRules.MaximumAttachmentCount} images.");

        var stored = new List<TaskAttachment>();
        var totalSize = existingSizeBytes;
        try
        {
            foreach (var upload in uploads)
            {
                if (upload.Length <= 0)
                    throw new ValidationException("Choose a non-empty image attachment.");
                if (upload.Length > TaskAttachmentRules.MaximumFileSizeBytes)
                    throw new ValidationException("Each task image must be 5 MB or smaller.");

                var content = await ReadImageAsync(upload);
                totalSize += content.Bytes.LongLength;
                if (totalSize > TaskAttachmentRules.MaximumTotalSizeBytes)
                    throw new ValidationException("Task attachments cannot exceed 25 MB in total.");

                var extension = content.ContentType switch
                {
                    "image/png" => ".png",
                    "image/jpeg" => ".jpg",
                    "image/gif" => ".gif",
                    "image/webp" => ".webp",
                    _ => throw new ValidationException("Only PNG, JPEG, GIF, and WebP images are supported.")
                };
                var storageKey = $"{Guid.NewGuid():N}{extension}";
                await using var imageStream = new MemoryStream(content.Bytes, writable: false);
                await attachmentStorage.SaveAsync(storageKey, imageStream);

                stored.Add(new TaskAttachment
                {
                    Id = Guid.NewGuid(),
                    TaskId = taskId,
                    UploadedByUserId = uploaderUserId,
                    BugReproductionStepId = bugReproductionStepId,
                    OriginalFileName = SafeFileName(upload.FileName, extension),
                    ContentType = content.ContentType,
                    SizeBytes = content.Bytes.LongLength,
                    StorageKey = storageKey,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return stored;
        }
        catch
        {
            await DeleteStoredAttachmentsAsync(stored);
            throw;
        }
    }

    private static async Task<ValidatedImage> ReadImageAsync(TaskAttachmentUpload upload)
    {
        await using var input = upload.OpenReadStream();
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer);
            if (read == 0) break;
            if (output.Length + read > TaskAttachmentRules.MaximumFileSizeBytes)
                throw new ValidationException("Each task image must be 5 MB or smaller.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }

        var bytes = output.ToArray();
        if (bytes.Length == 0)
            throw new ValidationException("Choose a non-empty image attachment.");
        var detectedType = DetectImageContentType(bytes)
            ?? throw new ValidationException("Only valid PNG, JPEG, GIF, and WebP images are supported.");
        var declaredType = upload.ContentType.Split(';', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(declaredType) &&
            !string.Equals(declaredType, detectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("The attachment content does not match its image type.");
        }

        return new ValidatedImage(bytes, detectedType);
    }

    private async Task DeleteStoredAttachmentsAsync(IEnumerable<TaskAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            try
            {
                await attachmentStorage.DeleteAsync(attachment.StorageKey);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Task attachment file {StorageKey} could not be cleaned up.", attachment.StorageKey);
            }
        }
    }

    private static string? DetectImageContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 6 &&
            (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
            return "image/gif";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    private static string SafeFileName(string fileName, string extension)
    {
        var safe = Path.GetFileName(fileName.Replace('/', Path.DirectorySeparatorChar)).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = $"task-image{extension}";
        return safe.Length <= 255 ? safe : safe[..255];
    }

    private static void QueueInboxItem(
        CalendarDbContext db,
        LumaTask task,
        Guid actorUserId,
        Guid? recipientUserId,
        InboxActivityType activityType,
        string message)
    {
        if (recipientUserId is null || recipientUserId == actorUserId) return;

        db.InboxItems.Add(new InboxItem
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipientUserId.Value,
            ActorUserId = actorUserId,
            TaskId = task.Id,
            ActivityType = activityType,
            Message = message,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void QueueMentionInboxItems(
        CalendarDbContext db,
        LumaTask task,
        Guid actorUserId,
        string message,
        IEnumerable<Guid> mentionedUserIds)
    {
        foreach (var userId in mentionedUserIds.Distinct())
        {
            QueueInboxItem(
                db,
                task,
                actorUserId,
                userId,
                InboxActivityType.TaskMentioned,
                message);
        }
    }

    private static async Task<ResolvedTaskMentions> ResolveMentionsAsync(
        CalendarDbContext db,
        string? description,
        IReadOnlyCollection<Guid>? descriptionMentionUserIds)
    {
        var resolved = await ResolveVisibleMentionsAsync(
            db,
            description,
            descriptionMentionUserIds,
            10000,
            "Task description cannot exceed 10000 characters.");
        var createdAt = DateTime.UtcNow;
        var mentions = resolved.UserIds
            .Select(userId => new TaskMention
            {
                UserId = userId,
                CreatedAt = createdAt
            })
            .ToArray();

        return new ResolvedTaskMentions(resolved.Text, mentions);
    }

    private static async Task<ResolvedVisibleMentions> ResolveVisibleMentionsAsync(
        CalendarDbContext db,
        string? text,
        IReadOnlyCollection<Guid>? selectedUserIds,
        int maximumLength,
        string maximumLengthMessage)
    {
        var trimmedText = text?.Trim() ?? string.Empty;
        var legacyMentions = TaskMentionSyntax.Parse(trimmedText);
        var requestedUserIds = (selectedUserIds ?? [])
            .Concat(legacyMentions.Select(mention => mention.UserId))
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .ToArray();

        var users = await db.Users.AsNoTracking()
            .Select(user => new { user.Id, user.Name })
            .ToDictionaryAsync(user => user.Id, user => user.Name);
        if (requestedUserIds.Any(userId => !users.ContainsKey(userId)))
            throw new ValidationException("One or more mentioned LUMA users no longer exist.");

        var canonicalText = TaskMentionSyntax.Canonicalize(trimmedText, users);
        if (canonicalText.Length > maximumLength)
            throw new ValidationException(maximumLengthMessage);

        var inferredUserIds = TaskMentionSyntax.FindUniqueVisibleMentionUserIds(canonicalText, users);
        var resolvedUserIds = requestedUserIds
            .Concat(inferredUserIds)
            .Distinct()
            .Where(userId => TaskMentionSyntax.ContainsVisibleMention(canonicalText, users[userId]))
            .ToArray();

        return new ResolvedVisibleMentions(canonicalText, resolvedUserIds);
    }

    private static string WorkStatusName(TaskWorkStatus status) => status switch
    {
        TaskWorkStatus.InProgress => "In Progress",
        TaskWorkStatus.Done => "Done",
        _ => "To Do"
    };

    private static async Task<LumaTask> LoadTaskForActionAsync(CalendarDbContext db, Guid taskId) =>
        await db.Tasks
            .Include(item => item.Creator)
            .Include(item => item.Assignee)
            .Include(item => item.Invitation)
            .Include(item => item.Project)
            .Include(item => item.Attachments)
            .Include(item => item.Mentions)
                .ThenInclude(mention => mention.User)
            .Include(item => item.BugDetails)
            .Include(item => item.ReproductionSteps)
                .ThenInclude(step => step.Attachments)
            .SingleOrDefaultAsync(item => item.Id == taskId)
        ?? throw new LumaTaskNotFoundException();

    private static async Task SaveActionAsync(CalendarDbContext db, string conflictMessage)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ValidationException(conflictMessage, exception);
        }
    }

    private static DeadlineRequestSnapshot ActiveDeadlineRequest(LumaTask task)
    {
        if (task.AssignmentStatus != TaskAssignmentStatus.DeadlineChangeRequested ||
            task.Deadline is null || task.RequestedDeadline is null || task.DeadlineChangeRequestedAt is null)
        {
            throw new ValidationException("There is no active deadline-change request to review.");
        }

        return new DeadlineRequestSnapshot(
            task.Deadline.Value,
            task.RequestedDeadline.Value,
            task.DeadlineChangeComment ?? string.Empty,
            task.DeadlineChangeRequestedAt.Value);
    }

    private static void ClearDeadlineRequest(LumaTask task)
    {
        task.RequestedDeadline = null;
        task.DeadlineChangeComment = null;
        task.DeadlineChangeRequestedAt = null;
    }

    private static LumaTaskDetails ToDetails(LumaTask task, Guid currentUserId)
    {
        var mentions = task.Mentions
            .Select(mention => new TaskMentionDetails(
                mention.UserId,
                mention.User?.Name ?? "LUMA user"))
            .ToArray();
        var mentionNames = mentions.GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key, group => group.First().UserName);

        return new LumaTaskDetails(
            task.Id,
            task.Title,
            TaskMentionSyntax.Canonicalize(task.Description, mentionNames),
            task.Creator!.Name,
            task.Assignee?.Name ?? task.Invitation?.RecipientEmail ?? "Unassigned",
            task.AssigneeId is null && task.Invitation?.Status == TaskInvitationStatus.Pending,
            task.ProjectId,
            task.Project?.Name ?? string.Empty,
            task.Deadline,
            task.CreatedAt,
            task.AssignmentStatus,
            task.WorkStatus,
            task.Priority,
            task.AcceptedAt,
            task.RequestedDeadline,
            task.DeadlineChangeComment ?? string.Empty,
            task.DeadlineChangeRequestedAt,
            task.Version,
            task.AssigneeId == currentUserId,
            task.CreatorId == currentUserId,
            task.CreatorId == currentUserId,
            task.AssigneeId == currentUserId,
            task.CreatorId == currentUserId || task.AssigneeId == currentUserId,
            task.AssigneeId is null && task.Invitation is null,
            task.Attachments
                .Where(attachment => attachment.BugReproductionStepId == null)
                .OrderBy(attachment => attachment.CreatedAt)
                .Select(attachment => new TaskAttachmentDetails(
                    attachment.Id,
                    attachment.OriginalFileName,
                    attachment.ContentType,
                    attachment.SizeBytes,
                    attachment.CreatedAt))
                .ToArray(),
            mentions,
            task.WorkItemType,
            task.BugCategory,
            task.BugSeverity,
            task.BugReproducibility,
            task.FoundInVersion ?? string.Empty,
            task.BugEnvironment ?? string.Empty,
            ToBugDetailsInput(task.BugDetails),
            task.ReproductionSteps
                .OrderBy(step => step.Position)
                .Select(step => new BugReproductionStepDetails(
                    step.Id,
                    step.Position,
                    step.Content,
                    step.ObservedResult ?? string.Empty,
                    step.IsPrimaryFailure,
                    step.Attachments.OrderBy(image => image.CreatedAt)
                        .Select(image => new TaskAttachmentDetails(
                            image.Id, image.OriginalFileName, image.ContentType, image.SizeBytes, image.CreatedAt))
                        .ToArray()))
                .ToArray(),
            string.IsNullOrWhiteSpace(task.BugDetails?.ReproductionMarkdown)
                ? BugReproductionMarkdown.FromLegacySteps(
                    task.ReproductionSteps.OrderBy(step => step.Position).Select(ToReproductionStepDetails))
                : task.BugDetails.ReproductionMarkdown);
    }

    private async Task<Guid> GetCurrentUserIdAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            throw new UnauthorizedAccessException("You must sign in to access tasks.");
        }

        return userId;
    }

    private static void Validate(CreateLumaTaskRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Title))
            errors.Add("Task title is required.");
        else if (request.Title.Trim().Length > 180)
            errors.Add("Task title cannot exceed 180 characters.");

        if ((request.Description?.Length ?? 0) > 10000)
            errors.Add("Task description cannot exceed 10000 characters.");
        if (TaskMarkdownImageSyntax.ContainsEmbeddedDataImage(request.Description))
            errors.Add("Paste or upload task images instead of embedding image data in the description.");
        ValidateReproductionMarkdown(errors, request.WorkItemType, request.ReproductionMarkdown);
        if (request.AssigneeId == Guid.Empty)
            errors.Add("Choose a valid task assignee.");
        else if (request.AssigneeId is null && !string.IsNullOrWhiteSpace(request.AssigneeEmail) &&
                 !IsValidEmail(request.AssigneeEmail!))
            errors.Add("Enter a valid assignee email address.");
        if (request.Deadline is not null && request.Deadline.Value < DateOnly.FromDateTime(DateTime.Today))
            errors.Add("Task deadline cannot be before today.");
        if (!Enum.IsDefined(request.Priority))
            errors.Add("Choose a valid task priority.");
        ValidateBugMetadata(
            errors,
            request.WorkItemType,
            request.BugCategory,
            request.BugSeverity,
            request.BugReproducibility,
            request.FoundInVersion,
            request.BugEnvironment);
        ValidateAdaptiveBugDetails(
            errors, request.WorkItemType, request.BugDetails,
            request.WorkItemType == WorkItemType.Bug
                ? ReconcileMarkdownSteps(NormalizeReproductionMarkdown(request.ReproductionMarkdown), request.ReproductionSteps, [])
                : request.ReproductionSteps,
            isCreate: true);
        if (request.ProjectId == Guid.Empty)
            errors.Add("Choose an existing LUMA project.");

        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));
    }

    private static void ValidateDeadlineChangeRequest(DateOnly currentDeadline, RequestTaskDeadlineChange request)
    {
        var errors = new List<string>();
        if (request.ProposedDeadline is null)
            errors.Add("Choose a proposed deadline.");
        else if (request.ProposedDeadline.Value < DateOnly.FromDateTime(DateTime.Today))
            errors.Add("The proposed deadline cannot be before today.");
        else if (request.ProposedDeadline.Value == currentDeadline)
            errors.Add("Choose a deadline different from the current deadline.");

        if ((request.Comment?.Trim().Length ?? 0) > 1000)
            errors.Add("The deadline-change comment cannot exceed 1000 characters.");

        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));
    }

    private static void ValidateContentUpdate(UpdateLumaTaskContentRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Title))
            errors.Add("Task title is required.");
        else if (request.Title.Trim().Length > 180)
            errors.Add("Task title cannot exceed 180 characters.");

        if ((request.Description?.Trim().Length ?? 0) > 10000)
            errors.Add("Task description cannot exceed 10000 characters.");
        if (TaskMarkdownImageSyntax.ContainsEmbeddedDataImage(request.Description))
            errors.Add("Paste or upload task images instead of embedding image data in the description.");
        ValidateReproductionMarkdown(errors, WorkItemType.Bug, request.ReproductionMarkdown);
        if (request.Priority is not null && !Enum.IsDefined(request.Priority.Value))
            errors.Add("Choose a valid task priority.");
        if (request.ProjectId == Guid.Empty)
            errors.Add("Choose an existing LUMA project.");
        if (request.Version == Guid.Empty)
            errors.Add("Reopen the task before saving changes.");

        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));
    }

    private static void ValidateBugMetadata(
        ICollection<string> errors,
        WorkItemType workItemType,
        BugCategory? category,
        BugSeverity? severity,
        BugReproducibility? reproducibility,
        string? foundInVersion,
        string? environment)
    {
        if (!Enum.IsDefined(workItemType))
        {
            errors.Add("Choose a valid work item type.");
            return;
        }

        if (workItemType == WorkItemType.Task)
        {
            if (category is not null || severity is not null || reproducibility is not null ||
                !string.IsNullOrWhiteSpace(foundInVersion) || !string.IsNullOrWhiteSpace(environment))
                errors.Add("Bug details can only be saved for Bug work items.");
            return;
        }

        if (category is null || !Enum.IsDefined(category.Value))
            errors.Add("Choose a valid bug category.");
        if (severity is null || !Enum.IsDefined(severity.Value))
            errors.Add("Choose a valid bug severity.");
        if (reproducibility is null || !Enum.IsDefined(reproducibility.Value))
            errors.Add("Choose how often the bug reproduces.");
        if ((foundInVersion?.Trim().Length ?? 0) > 80)
            errors.Add("Found in version cannot exceed 80 characters.");
        if ((environment?.Trim().Length ?? 0) > 500)
            errors.Add("Bug environment cannot exceed 500 characters.");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateAdaptiveBugDetails(
        ICollection<string> errors,
        WorkItemType workItemType,
        BugAdaptiveDetailsInput? details,
        IReadOnlyList<BugReproductionStepInput>? steps,
        bool isCreate)
    {
        if (workItemType == WorkItemType.Task)
        {
            if (details is not null || (steps?.Count ?? 0) > 0)
                errors.Add("Adaptive bug details can only be saved for Bug work items.");
            return;
        }

        ValidateOptionalLength(errors, details?.ExpectedResult, 4000, "Expected result");
        ValidateOptionalLength(errors, details?.ObservedResult, 4000, "Observed result");
        ValidateOptionalLength(errors, details?.ErrorMessage, 2000, "Error message");
        ValidateOptionalLength(errors, details?.ErrorDetails, 10000, "Logs or stack trace");
        ValidateOptionalLength(errors, details?.Logs, 10000, "Logs");
        ValidateOptionalLength(errors, details?.ExpectedDuration, 100, "Expected duration");
        ValidateOptionalLength(errors, details?.ActualDuration, 100, "Actual duration");
        ValidateOptionalLength(errors, details?.HttpMethod, 12, "HTTP method");
        ValidateOptionalLength(errors, details?.Endpoint, 2000, "Endpoint");
        ValidateOptionalLength(errors, details?.ApiRequest, 10000, "API request");
        ValidateOptionalLength(errors, details?.ApiResponse, 10000, "API response");
        ValidateOptionalLength(errors, details?.CorrelationId, 200, "Correlation ID");
        ValidateOptionalLength(errors, details?.DataEntity, 200, "Entity or record");
        ValidateOptionalLength(errors, details?.DataIdentifier, 500, "Identifier");
        ValidateOptionalLength(errors, details?.ExpectedValue, 4000, "Expected value");
        ValidateOptionalLength(errors, details?.ActualValue, 4000, "Actual value");
        ValidateOptionalLength(errors, details?.LastKnownGoodVersion, 80, "Last known good version");
        ValidateOptionalLength(errors, details?.FirstBrokenVersion, 80, "First broken version");
        ValidateOptionalLength(errors, details?.WorksOn, 500, "Works on");
        ValidateOptionalLength(errors, details?.FailsOn, 500, "Fails on");
        if (details?.Attempts is <= 0 or > 100000)
            errors.Add("Attempts must be between 1 and 100000.");
        if (details?.StatusCode is < 100 or > 599)
            errors.Add("Choose a valid HTTP status code.");

        var submittedSteps = steps ?? [];
        if (submittedSteps.Count > 50)
            errors.Add("A bug can have up to 50 reproduction steps.");
        if (submittedSteps.Count(step => step.IsPrimaryFailure) > 1)
            errors.Add("Only one reproduction step can be marked as the primary failure.");
        foreach (var step in submittedSteps)
        {
            if (isCreate && step.Id is not null)
                errors.Add("New reproduction steps cannot supply an existing step identifier.");
            if (string.IsNullOrWhiteSpace(step.Content))
                errors.Add("Each reproduction step needs an instruction.");
            else if (step.Content.Trim().Length > 4000)
                errors.Add("A reproduction step cannot exceed 4000 characters.");
            if ((step.ObservedResult?.Trim().Length ?? 0) > 2000)
                errors.Add("A reproduction step observed result cannot exceed 2000 characters.");
        }
    }

    private static void ValidateOptionalLength(ICollection<string> errors, string? value, int maximum, string label)
    {
        if ((value?.Trim().Length ?? 0) > maximum)
            errors.Add($"{label} cannot exceed {maximum} characters.");
    }

    private static LumaTaskBugDetails ToBugDetailsEntity(BugAdaptiveDetailsInput? input)
    {
        input ??= new BugAdaptiveDetailsInput();
        return new LumaTaskBugDetails
        {
            ExpectedResult = NormalizeOptional(input.ExpectedResult),
            ObservedResult = NormalizeOptional(input.ObservedResult),
            ErrorMessage = NormalizeOptional(input.ErrorMessage),
            ErrorDetails = NormalizeOptional(input.ErrorDetails),
            Logs = NormalizeOptional(input.Logs),
            ExpectedDuration = NormalizeOptional(input.ExpectedDuration),
            ActualDuration = NormalizeOptional(input.ActualDuration),
            Attempts = input.Attempts,
            HttpMethod = NormalizeOptional(input.HttpMethod)?.ToUpperInvariant(),
            Endpoint = NormalizeOptional(input.Endpoint),
            StatusCode = input.StatusCode,
            ApiRequest = NormalizeOptional(input.ApiRequest),
            ApiResponse = NormalizeOptional(input.ApiResponse),
            CorrelationId = NormalizeOptional(input.CorrelationId),
            DataEntity = NormalizeOptional(input.DataEntity),
            DataIdentifier = NormalizeOptional(input.DataIdentifier),
            ExpectedValue = NormalizeOptional(input.ExpectedValue),
            ActualValue = NormalizeOptional(input.ActualValue),
            LastKnownGoodVersion = NormalizeOptional(input.LastKnownGoodVersion),
            FirstBrokenVersion = NormalizeOptional(input.FirstBrokenVersion),
            WorksOn = NormalizeOptional(input.WorksOn),
            FailsOn = NormalizeOptional(input.FailsOn)
        };
    }

    private static BugAdaptiveDetailsInput? ToBugDetailsInput(LumaTaskBugDetails? details) => details is null ? null : new(
        details.ExpectedResult,
        details.ObservedResult,
        details.ErrorMessage,
        details.ErrorDetails,
        details.ExpectedDuration,
        details.ActualDuration,
        details.Attempts,
        details.HttpMethod,
        details.Endpoint,
        details.StatusCode,
        details.ApiRequest,
        details.ApiResponse,
        details.CorrelationId,
        details.DataEntity,
        details.DataIdentifier,
        details.ExpectedValue,
        details.ActualValue,
        details.LastKnownGoodVersion,
        details.FirstBrokenVersion,
        details.WorksOn,
        details.FailsOn,
        details.Logs);

    private static void ApplyBugDetails(LumaTaskBugDetails target, BugAdaptiveDetailsInput? input)
    {
        var normalized = ToBugDetailsEntity(input);
        target.ExpectedResult = normalized.ExpectedResult;
        target.ObservedResult = normalized.ObservedResult;
        target.ErrorMessage = normalized.ErrorMessage;
        target.ErrorDetails = normalized.ErrorDetails;
        target.Logs = normalized.Logs;
        target.ExpectedDuration = normalized.ExpectedDuration;
        target.ActualDuration = normalized.ActualDuration;
        target.Attempts = normalized.Attempts;
        target.HttpMethod = normalized.HttpMethod;
        target.Endpoint = normalized.Endpoint;
        target.StatusCode = normalized.StatusCode;
        target.ApiRequest = normalized.ApiRequest;
        target.ApiResponse = normalized.ApiResponse;
        target.CorrelationId = normalized.CorrelationId;
        target.DataEntity = normalized.DataEntity;
        target.DataIdentifier = normalized.DataIdentifier;
        target.ExpectedValue = normalized.ExpectedValue;
        target.ActualValue = normalized.ActualValue;
        target.LastKnownGoodVersion = normalized.LastKnownGoodVersion;
        target.FirstBrokenVersion = normalized.FirstBrokenVersion;
        target.WorksOn = normalized.WorksOn;
        target.FailsOn = normalized.FailsOn;
    }

    private static bool ReproductionStepsEqual(
        IEnumerable<BugReproductionStep> existing,
        IReadOnlyList<BugReproductionStepInput> submitted)
    {
        var current = existing.OrderBy(step => step.Position).ToArray();
        if (current.Length != submitted.Count) return false;
        for (var index = 0; index < current.Length; index++)
        {
            var left = current[index];
            var right = submitted[index];
            if (right.Id != left.Id ||
                !string.Equals(left.Content, right.Content.Trim(), StringComparison.Ordinal) ||
                !string.Equals(left.ObservedResult, NormalizeOptional(right.ObservedResult), StringComparison.Ordinal) ||
                left.IsPrimaryFailure != right.IsPrimaryFailure ||
                (right.NewImages?.Count ?? 0) > 0 ||
                (right.RemovedImageIds?.Count ?? 0) > 0)
                return false;
        }
        return true;
    }

    private static IReadOnlyList<BugReproductionStepInput> ReconcileMarkdownSteps(
        string? markdown,
        IReadOnlyList<BugReproductionStepInput>? submitted,
        IReadOnlyList<BugReproductionStepDetails> existing)
    {
        if (markdown is null) return submitted ?? [];
        var parsed = BugReproductionMarkdown.Parse(markdown);
        var result = new List<BugReproductionStepInput>(parsed.Count);
        foreach (var parsedStep in parsed)
        {
            var submittedMatches = (submitted ?? [])
                .Where(item => BugReproductionMarkdown.MatchKey(item.Content) == BugReproductionMarkdown.MatchKey(parsedStep.Markdown))
                .ToArray();
            var existingMatches = existing
                .Where(item => BugReproductionMarkdown.MatchKey(item.Content) == BugReproductionMarkdown.MatchKey(parsedStep.Markdown))
                .ToArray();
            var source = submittedMatches.Length == 1 ? submittedMatches[0] : null;
            var prior = existingMatches.Length == 1 ? existingMatches[0] : null;
            result.Add(new BugReproductionStepInput(
                source?.Id ?? prior?.Id,
                parsedStep.Markdown,
                source?.ObservedResult ?? prior?.ObservedResult,
                source?.IsPrimaryFailure ?? prior?.IsPrimaryFailure ?? false,
                source?.NewImages,
                source?.RemovedImageIds));
        }
        if (result.Count(item => item.IsPrimaryFailure) > 1)
            result = result.Select(item => item with { IsPrimaryFailure = false }).ToList();
        return result;
    }

    private static BugReproductionStepDetails ToReproductionStepDetails(BugReproductionStep step) => new(
        step.Id, step.Position, step.Content, step.ObservedResult ?? string.Empty, step.IsPrimaryFailure,
        step.Attachments.OrderBy(image => image.CreatedAt)
            .Select(image => new TaskAttachmentDetails(image.Id, image.OriginalFileName, image.ContentType, image.SizeBytes, image.CreatedAt))
            .ToArray());

    private static string? NormalizeReproductionMarkdown(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? null : markdown.Trim();

    private static void ValidateReproductionMarkdown(ICollection<string> errors, WorkItemType itemType, string? markdown)
    {
        if (markdown is null) return;
        if (itemType != WorkItemType.Bug)
        {
            errors.Add("Reproduction steps can only be saved for Bug work items.");
            return;
        }
        if (markdown.Length > BugReproductionMarkdown.MaximumLength)
            errors.Add($"Reproduction steps cannot exceed {BugReproductionMarkdown.MaximumLength} characters.");
        if (TaskMarkdownImageSyntax.ContainsEmbeddedDataImage(markdown))
            errors.Add("Paste or upload reproduction images instead of embedding image data.");
    }

    private static void EnsureCurrentVersion(LumaTask task, Guid version)
    {
        if (version == Guid.Empty || task.Version != version)
            throw new ValidationException("This task changed in another session. Reopen it and try again.");
    }

    private static bool IsAllowedWorkStatusTransition(TaskWorkStatus current, TaskWorkStatus next) =>
        (current, next) is
            (TaskWorkStatus.ToDo, TaskWorkStatus.InProgress) or
            (TaskWorkStatus.InProgress, TaskWorkStatus.Done);

    private static IQueryable<LumaTask> ApplyListFilters(IQueryable<LumaTask> tasks, TaskListQuery query)
    {
        var search = query.Search?.Trim().ToLower();
        if (!string.IsNullOrWhiteSpace(search))
            tasks = tasks.Where(task => task.Title.ToLower().Contains(search));
        if (query.WorkStatus is not null)
            tasks = tasks.Where(task => task.WorkStatus == query.WorkStatus.Value);
        if (query.AssignmentStatus is not null)
            tasks = tasks.Where(task => task.AssignmentStatus == query.AssignmentStatus.Value);
        if (query.Priority is not null)
            tasks = tasks.Where(task => task.Priority == query.Priority.Value);
        if (query.ProjectId is not null)
            tasks = tasks.Where(task => task.ProjectId == query.ProjectId.Value);
        var assigneeIds = (query.AssigneeIds ?? [])
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (assigneeIds.Length > 0 && query.IncludeUnassigned)
            tasks = tasks.Where(task =>
                (task.AssigneeId != null && assigneeIds.Contains(task.AssigneeId.Value)) ||
                (task.AssigneeId == null && task.Invitation == null));
        else if (assigneeIds.Length > 0)
            tasks = tasks.Where(task =>
                task.AssigneeId != null && assigneeIds.Contains(task.AssigneeId.Value));
        else if (query.IncludeUnassigned)
            tasks = tasks.Where(task => task.AssigneeId == null && task.Invitation == null);

        var today = DateOnly.FromDateTime(DateTime.Today);
        tasks = query.Deadline switch
        {
            TaskDeadlineFilter.NoDeadline => tasks.Where(task => task.Deadline == null),
            TaskDeadlineFilter.Overdue => tasks.Where(task => task.Deadline < today),
            TaskDeadlineFilter.Today => tasks.Where(task => task.Deadline == today),
            TaskDeadlineFilter.ThisWeek => tasks.Where(task =>
                task.Deadline >= today && task.Deadline <= EndOfWeek(today)),
            _ => tasks
        };
        return tasks;
    }

    private static IOrderedQueryable<LumaTask> ApplyListSort(IQueryable<LumaTask> tasks, TaskSortOrder sort) =>
        sort switch
        {
            TaskSortOrder.PriorityHighest => tasks
                .OrderByDescending(task => task.Priority)
                .ThenBy(task => task.Deadline == null)
                .ThenBy(task => task.Deadline)
                .ThenByDescending(task => task.CreatedAt),
            TaskSortOrder.Newest => tasks
                .OrderByDescending(task => task.CreatedAt)
                .ThenBy(task => task.Deadline),
            _ => tasks
                .OrderBy(task => task.Deadline == null)
                .ThenBy(task => task.Deadline)
                .ThenByDescending(task => task.Priority)
                .ThenBy(task => task.CreatedAt)
        };

    private static DateOnly EndOfWeek(DateOnly date)
    {
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilSunday);
    }

    private static void EnsureTaskAccess(Guid creatorId, Guid? assigneeId, Guid currentUserId)
    {
        if (creatorId != currentUserId && assigneeId != currentUserId)
            throw new UnauthorizedAccessException("You do not have access to this task.");
    }

    private static string ValidateComment(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
            throw new ValidationException("Write a comment before sending.");
        if (value.Length > 2000)
            throw new ValidationException("Task comments cannot exceed 2000 characters.");
        return value;
    }

    private static string PreviewComment(string text)
    {
        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 120 ? singleLine : $"{singleLine[..117]}…";
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static bool IsValidEmail(string email) =>
        email.Trim().Length <= 254 &&
        System.Net.Mail.MailAddress.TryCreate(email.Trim(), out var address) &&
        string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record TaskUser(Guid Id, string Name, string Email);
    private sealed record ValidatedImage(byte[] Bytes, string ContentType);
    private sealed record ResolvedTaskMentions(
        string Description,
        IReadOnlyList<TaskMention> Mentions);
    private sealed record ResolvedVisibleMentions(
        string Text,
        IReadOnlyList<Guid> UserIds);
    private sealed record DeadlineRequestSnapshot(
        DateOnly CurrentDeadline,
        DateOnly RequestedDeadline,
        string Comment,
        DateTime RequestedAt);
}
