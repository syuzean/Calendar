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
    Guid AssigneeId,
    DateOnly? Deadline);

public sealed record RequestTaskDeadlineChange(
    DateOnly? ProposedDeadline,
    string? Comment);

public sealed record UpdateLumaTaskContentRequest(
    string Title,
    string? Description,
    Guid Version);

public sealed record ChangeTaskWorkStatusRequest(
    TaskWorkStatus WorkStatus,
    Guid Version);

public sealed record AddTaskCommentRequest(string Text);

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
    DateOnly Deadline,
    TaskAssignmentStatus AssignmentStatus,
    TaskWorkStatus WorkStatus);

public sealed record CreatedLumaTask(
    Guid Id,
    string Title,
    string AssigneeName,
    DateOnly Deadline,
    TaskAssignmentStatus AssignmentStatus,
    TaskWorkStatus WorkStatus);

public sealed record LumaTaskDetails(
    Guid Id,
    string Title,
    string Description,
    string CreatorName,
    string AssigneeName,
    DateOnly Deadline,
    DateTime CreatedAt,
    TaskAssignmentStatus AssignmentStatus,
    TaskWorkStatus WorkStatus,
    DateTime? AcceptedAt,
    DateOnly? RequestedDeadline,
    string DeadlineChangeComment,
    DateTime? DeadlineChangeRequestedAt,
    Guid Version,
    bool CanAccept,
    bool CanReviewDeadlineChange,
    bool CanEdit,
    bool CanManageWorkStatus);

public sealed class LumaTaskNotFoundException : Exception
{
    public LumaTaskNotFoundException() : base("This task no longer exists.") { }
}

public sealed class TaskStore(
    IDbContextFactory<CalendarDbContext> dbFactory,
    AuthenticationStateProvider authenticationStateProvider,
    ITaskNotifier taskNotifier,
    ITaskLinkBuilder taskLinkBuilder,
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
        var users = await db.Users.AsNoTracking()
            .Where(user => user.Id == creatorId || user.Id == request.AssigneeId)
            .Select(user => new TaskUser(user.Id, user.Name, user.Email))
            .ToListAsync();

        var maker = users.SingleOrDefault(user => user.Id == creatorId);
        var doer = users.SingleOrDefault(user => user.Id == request.AssigneeId);
        if (maker is null)
            throw new UnauthorizedAccessException("The signed-in LUMA user could not be found.");
        if (doer is null)
            throw new ValidationException("Choose a registered LUMA user as the assignee.");

        var entity = new LumaTask
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            CreatorId = creatorId,
            AssigneeId = request.AssigneeId,
            Deadline = request.Deadline!.Value,
            CreatedAt = DateTime.UtcNow,
            AssignmentStatus = TaskAssignmentStatus.Pending,
            WorkStatus = TaskWorkStatus.ToDo,
            AcceptedAt = null,
            Version = Guid.NewGuid()
        };

        db.Tasks.Add(entity);
        await db.SaveChangesAsync();
        await NotifyCreatedAfterCommitAsync(entity, maker, doer);
        return entity.Id;
    }

    public async Task<IReadOnlyList<AssignedLumaTask>> LoadAssignedAsync()
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tasks.AsNoTracking()
            .Where(task => task.AssigneeId == currentUserId)
            .OrderBy(task => task.Deadline)
            .ThenBy(task => task.CreatedAt)
            .Select(task => new AssignedLumaTask(
                task.Id,
                task.Title,
                task.Creator!.Name,
                task.Deadline,
                task.AssignmentStatus,
                task.WorkStatus))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<CreatedLumaTask>> LoadCreatedAsync()
    {
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tasks.AsNoTracking()
            .Where(task => task.CreatorId == currentUserId)
            .OrderBy(task => task.Deadline)
            .ThenBy(task => task.CreatedAt)
            .Select(task => new CreatedLumaTask(
                task.Id,
                task.Title,
                task.Assignee!.Name,
                task.Deadline,
                task.AssignmentStatus,
                task.WorkStatus))
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
                AssigneeName = item.Assignee!.Name,
                item.Deadline,
                item.CreatedAt,
                item.AssignmentStatus,
                item.WorkStatus,
                item.AcceptedAt,
                item.RequestedDeadline,
                item.DeadlineChangeComment,
                item.DeadlineChangeRequestedAt,
                item.Version
            })
            .SingleOrDefaultAsync();

        if (task is null) throw new LumaTaskNotFoundException();
        if (task.CreatorId != currentUserId && task.AssigneeId != currentUserId)
            throw new UnauthorizedAccessException("You do not have access to this task.");

        return new LumaTaskDetails(
            task.Id,
            task.Title,
            task.Description,
            task.CreatorName,
            task.AssigneeName,
            task.Deadline,
            task.CreatedAt,
            task.AssignmentStatus,
            task.WorkStatus,
            task.AcceptedAt,
            task.RequestedDeadline,
            task.DeadlineChangeComment ?? string.Empty,
            task.DeadlineChangeRequestedAt,
            task.Version,
            task.AssigneeId == currentUserId,
            task.CreatorId == currentUserId,
            task.CreatorId == currentUserId,
            task.AssigneeId == currentUserId);
    }

    public async Task<LumaTaskDetails> AcceptAsync(Guid taskId)
    {
        LastNotice = null;
        var currentUserId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.Tasks
            .Include(item => item.Creator)
            .Include(item => item.Assignee)
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
        var acceptedByThisOperation = false;

        try
        {
            await db.SaveChangesAsync();
            acceptedByThisOperation = true;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await db.Entry(task).ReloadAsync();
            if (task.AssigneeId != currentUserId)
                throw new UnauthorizedAccessException("Only the assigned user can accept this task.");
            if (task.AssignmentStatus != TaskAssignmentStatus.Accepted)
                throw new ValidationException("The task changed before it could be accepted. Reopen it and try again.", exception);
        }

        if (acceptedByThisOperation)
            await NotifyAcceptedAfterCommitAsync(task);

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

        ValidateDeadlineChangeRequest(task.Deadline, request);
        task.RequestedDeadline = request.ProposedDeadline!.Value;
        task.DeadlineChangeComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        task.DeadlineChangeRequestedAt = DateTime.UtcNow;
        task.AssignmentStatus = TaskAssignmentStatus.DeadlineChangeRequested;
        task.AcceptedAt = null;
        task.Version = Guid.NewGuid();

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

        ValidateContentUpdate(request);
        EnsureCurrentVersion(task, request.Version);

        var title = request.Title.Trim();
        var description = request.Description?.Trim() ?? string.Empty;
        var titleChanged = !string.Equals(task.Title, title, StringComparison.Ordinal);
        var descriptionChanged = !string.Equals(task.Description, description, StringComparison.Ordinal);
        if (!titleChanged && !descriptionChanged)
            return ToDetails(task, currentUserId);

        var changes = new TaskContentChangeSnapshot(
            titleChanged,
            task.Title,
            title,
            descriptionChanged,
            task.Description,
            description);
        task.Title = title;
        task.Description = description;
        task.Version = Guid.NewGuid();

        await SaveActionAsync(db, "The task changed before your edits could be saved. Reopen it and try again.");
        await NotifyTaskUpdatedAfterCommitAsync(task, changes);
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

        var previousStatus = task.WorkStatus;
        task.WorkStatus = request.WorkStatus;
        task.Version = Guid.NewGuid();

        await SaveActionAsync(db, "The task changed before its progress could be updated. Reopen it and try again.");
        await NotifyWorkStatusChangedAfterCommitAsync(task, previousStatus);
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
        var comment = new LumaTaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            AuthorUserId = currentUserId,
            Text = text,
            CreatedAt = DateTime.UtcNow
        };

        db.TaskComments.Add(comment);
        await db.SaveChangesAsync();
        await NotifyCommentAddedAfterCommitAsync(task, author, comment);
        return new LumaTaskCommentDetails(
            comment.Id,
            comment.TaskId,
            comment.AuthorUserId,
            author.Name,
            comment.Text,
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
                task.Description,
                maker.Name,
                doer.Name,
                task.Deadline,
                taskLinkBuilder.Task(task.Id),
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task-created notifications could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The task was created, but one or more notification emails could not be sent.";
        }
    }

    private async Task NotifyAcceptedAfterCommitAsync(LumaTask task)
    {
        var maker = new TaskUser(task.Creator!.Id, task.Creator.Name, task.Creator.Email);
        var doer = new TaskUser(task.Assignee!.Id, task.Assignee.Name, task.Assignee.Email);
        var recipients = MakerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyAcceptedAsync(new TaskAcceptedNotification(
                task.Title,
                maker.Name,
                doer.Name,
                task.Deadline,
                task.AcceptedAt!.Value,
                taskLinkBuilder.Task(task.Id),
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task-accepted notifications could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The task was accepted, but one or more notification emails could not be sent.";
        }
    }

    private async Task NotifyDeadlineChangeRequestedAfterCommitAsync(LumaTask task)
    {
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
                task.Deadline,
                task.RequestedDeadline!.Value,
                task.DeadlineChangeComment ?? string.Empty,
                task.DeadlineChangeRequestedAt!.Value,
                taskLinkBuilder.Task(task.Id),
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deadline-change request notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The deadline change was requested, but its notification email could not be sent.";
        }
    }

    private async Task NotifyDeadlineChangeApprovedAfterCommitAsync(LumaTask task, DeadlineRequestSnapshot request)
    {
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
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deadline-change approval notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The deadline change was approved, but its notification email could not be sent.";
        }
    }

    private async Task NotifyDeadlineChangeDeclinedAfterCommitAsync(LumaTask task, DeadlineRequestSnapshot request)
    {
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
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deadline-change decline notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The deadline change was declined, but its notification email could not be sent.";
        }
    }

    private async Task NotifyTaskUpdatedAfterCommitAsync(LumaTask task, TaskContentChangeSnapshot changes)
    {
        var maker = User(task.Creator!);
        var doer = User(task.Assignee!);
        var recipients = DoerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyUpdatedAsync(new TaskUpdatedNotification(
                task.Title,
                maker.Name,
                doer.Name,
                task.Deadline,
                new TaskContentChanges(
                    changes.TitleChanged,
                    changes.PreviousTitle,
                    changes.UpdatedTitle,
                    changes.DescriptionChanged,
                    changes.PreviousDescription,
                    changes.UpdatedDescription),
                taskLinkBuilder.Task(task.Id),
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task-updated notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The task was updated, but its notification email could not be sent.";
        }
    }

    private async Task NotifyWorkStatusChangedAfterCommitAsync(LumaTask task, TaskWorkStatus previousStatus)
    {
        var maker = User(task.Creator!);
        var doer = User(task.Assignee!);
        var recipients = MakerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyWorkStatusChangedAsync(new TaskWorkStatusChangedNotification(
                task.Title,
                maker.Name,
                doer.Name,
                previousStatus,
                task.WorkStatus,
                task.Deadline,
                taskLinkBuilder.Task(task.Id),
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task work-status notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The task progress was updated, but its notification email could not be sent.";
        }
    }

    private async Task NotifyCommentAddedAfterCommitAsync(LumaTask task, AppUser author, LumaTaskComment comment)
    {
        var maker = User(task.Creator!);
        var doer = User(task.Assignee!);
        IReadOnlyList<TaskNotificationRecipient> recipients = task.CreatorId == task.AssigneeId
            ? []
            : author.Id == task.CreatorId
                ? DoerRecipients(maker, doer)
                : MakerRecipients(maker, doer);
        if (recipients.Count == 0) return;

        try
        {
            await taskNotifier.NotifyCommentAddedAsync(new TaskCommentAddedNotification(
                task.Title,
                author.Name,
                comment.Text,
                author.Id == task.CreatorId ? TaskNotificationRole.Maker : TaskNotificationRole.Doer,
                taskLinkBuilder.Task(task.Id),
                recipients));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Task-comment notification could not be sent for task {TaskId}.", task.Id);
            LastNotice = "The comment was saved, but its notification email could not be sent.";
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

    private static async Task<LumaTask> LoadTaskForActionAsync(CalendarDbContext db, Guid taskId) =>
        await db.Tasks
            .Include(item => item.Creator)
            .Include(item => item.Assignee)
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
            task.RequestedDeadline is null || task.DeadlineChangeRequestedAt is null)
        {
            throw new ValidationException("There is no active deadline-change request to review.");
        }

        return new DeadlineRequestSnapshot(
            task.Deadline,
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

    private static LumaTaskDetails ToDetails(LumaTask task, Guid currentUserId) => new(
        task.Id,
        task.Title,
        task.Description,
        task.Creator!.Name,
        task.Assignee!.Name,
        task.Deadline,
        task.CreatedAt,
        task.AssignmentStatus,
        task.WorkStatus,
        task.AcceptedAt,
        task.RequestedDeadline,
        task.DeadlineChangeComment ?? string.Empty,
        task.DeadlineChangeRequestedAt,
        task.Version,
        task.AssigneeId == currentUserId,
        task.CreatorId == currentUserId,
        task.CreatorId == currentUserId,
        task.AssigneeId == currentUserId);

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

        if ((request.Description?.Length ?? 0) > 4000)
            errors.Add("Task description cannot exceed 4000 characters.");
        if (request.AssigneeId == Guid.Empty)
            errors.Add("Task assignee is required.");
        if (request.Deadline is null)
            errors.Add("Task deadline is required.");
        else if (request.Deadline.Value < DateOnly.FromDateTime(DateTime.Today))
            errors.Add("Task deadline cannot be before today.");

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

        if ((request.Description?.Trim().Length ?? 0) > 4000)
            errors.Add("Task description cannot exceed 4000 characters.");
        if (request.Version == Guid.Empty)
            errors.Add("Reopen the task before saving changes.");

        if (errors.Count > 0)
            throw new ValidationException(string.Join(" ", errors));
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

    private static void EnsureTaskAccess(Guid creatorId, Guid assigneeId, Guid currentUserId)
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

    private sealed record TaskUser(Guid Id, string Name, string Email);
    private sealed record TaskContentChangeSnapshot(
        bool TitleChanged,
        string PreviousTitle,
        string UpdatedTitle,
        bool DescriptionChanged,
        string PreviousDescription,
        string UpdatedDescription);
    private sealed record DeadlineRequestSnapshot(
        DateOnly CurrentDeadline,
        DateOnly RequestedDeadline,
        string Comment,
        DateTime RequestedAt);
}
