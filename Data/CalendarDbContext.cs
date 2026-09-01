using Calendar.Models;
using Calendar.Services;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Data;

public sealed class CalendarDbContext(DbContextOptions<CalendarDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventInvitation> EventInvitations => Set<EventInvitation>();
    public DbSet<LumaTask> Tasks => Set<LumaTask>();
    public DbSet<LumaTaskComment> TaskComments => Set<LumaTaskComment>();
    public DbSet<TaskInvitation> TaskInvitations => Set<TaskInvitation>();
    public DbSet<LumaProject> Projects => Set<LumaProject>();
    public DbSet<InboxItem> InboxItems => Set<InboxItem>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskMention> TaskMentions => Set<TaskMention>();
    public DbSet<TaskCommentMention> TaskCommentMentions => Set<TaskCommentMention>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
            entity.Property(user => user.Name).HasMaxLength(80);
            entity.Property(user => user.Email).HasMaxLength(254);
            entity.Property(user => user.NormalizedEmail).HasMaxLength(254);
            entity.Property(user => user.PasswordHash).HasMaxLength(512);
        });

        modelBuilder.Entity<CalendarEvent>(entity =>
        {
            entity.Property(item => item.Title).HasMaxLength(180);
            entity.Property(item => item.Description).HasMaxLength(4000);
            entity.Property(item => item.MeetingUrl).HasMaxLength(MeetingUrlHelper.MaximumLength);
            entity.Property(item => item.Color).HasMaxLength(20);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.Start);
            entity.HasIndex(item => new { item.OwnerId, item.Start });
            entity.HasOne(item => item.Owner)
                .WithMany(user => user.OwnedEvents)
                .HasForeignKey(item => item.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventParticipant>(entity =>
        {
            entity.HasKey(item => new { item.EventId, item.UserId });
            entity.HasOne(item => item.Event)
                .WithMany(calendarEvent => calendarEvent.Participants)
                .HasForeignKey(item => item.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.User)
                .WithMany(user => user.Participations)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<EventInvitation>(entity =>
        {
            entity.HasIndex(invitation => invitation.TokenHash).IsUnique();
            entity.HasIndex(invitation => new { invitation.EventId, invitation.NormalizedRecipientEmail }).IsUnique();
            entity.HasIndex(invitation => new { invitation.ClaimedByUserId, invitation.Status });
            entity.Property(invitation => invitation.RecipientEmail).HasColumnName("InvitedEmail").HasMaxLength(254);
            entity.Property(invitation => invitation.NormalizedRecipientEmail).HasColumnName("NormalizedEmail").HasMaxLength(254);
            entity.Property(invitation => invitation.ClaimedByUserId).HasColumnName("InvitedUserId");
            entity.Property(invitation => invitation.ClaimedUtc).HasColumnName("RespondedUtc");
            entity.Property(invitation => invitation.TokenHash).HasMaxLength(64);
            entity.Property(invitation => invitation.ResponseComment).HasMaxLength(1000);
            entity.Property(invitation => invitation.RowVersion).IsRowVersion();
            entity.Property(invitation => invitation.EmailLastError).HasMaxLength(1000);
            entity.HasOne(invitation => invitation.Event)
                .WithMany(calendarEvent => calendarEvent.Invitations)
                .HasForeignKey(invitation => invitation.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(invitation => invitation.ClaimedByUser)
                .WithMany(user => user.ClaimedInvitations)
                .HasForeignKey(invitation => invitation.ClaimedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LumaTask>(entity =>
        {
            entity.Property(task => task.Title).HasMaxLength(180);
            entity.Property(task => task.Description).HasMaxLength(10000);
            entity.Property(task => task.DeadlineChangeComment).HasMaxLength(1000);
            entity.Property(task => task.Deadline).HasColumnType("date");
            entity.Property(task => task.RequestedDeadline).HasColumnType("date");
            entity.Property(task => task.Version).IsConcurrencyToken();
            entity.ToTable("Tasks", table =>
            {
                table.HasCheckConstraint("CK_Tasks_AssignmentStatus", "[AssignmentStatus] IN (0, 1, 2)");
                table.HasCheckConstraint("CK_Tasks_WorkStatus", "[WorkStatus] IN (0, 1, 2)");
                table.HasCheckConstraint("CK_Tasks_Priority", "[Priority] IN (0, 1, 2, 3, 4)");
            });
            entity.HasOne(task => task.Creator)
                .WithMany(user => user.CreatedTasks)
                .HasForeignKey(task => task.CreatorId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(task => task.Assignee)
                .WithMany(user => user.AssignedTasks)
                .HasForeignKey(task => task.AssigneeId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(task => task.ProjectId);
            entity.HasOne(task => task.Project)
                .WithMany(project => project.Tasks)
                .HasForeignKey(task => task.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LumaTaskComment>(entity =>
        {
            entity.ToTable("TaskComments");
            entity.Property(comment => comment.Text).HasMaxLength(2000);
            entity.HasIndex(comment => new { comment.TaskId, comment.CreatedAt });
            entity.HasOne(comment => comment.Task)
                .WithMany(task => task.Comments)
                .HasForeignKey(comment => comment.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(comment => comment.Author)
                .WithMany(user => user.TaskComments)
                .HasForeignKey(comment => comment.AuthorUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TaskInvitation>(entity =>
        {
            entity.ToTable("TaskInvitations", table =>
                table.HasCheckConstraint("CK_TaskInvitations_Status", "[Status] IN (0, 1, 2)"));
            entity.HasIndex(invitation => invitation.TaskId).IsUnique();
            entity.HasIndex(invitation => invitation.TokenHash).IsUnique();
            entity.HasIndex(invitation => new { invitation.NormalizedRecipientEmail, invitation.Status });
            entity.Property(invitation => invitation.RecipientEmail).HasMaxLength(254);
            entity.Property(invitation => invitation.NormalizedRecipientEmail).HasMaxLength(254);
            entity.Property(invitation => invitation.TokenHash).HasMaxLength(64);
            entity.Property(invitation => invitation.RowVersion).IsRowVersion();
            entity.HasOne(invitation => invitation.Task)
                .WithOne(task => task.Invitation)
                .HasForeignKey<TaskInvitation>(invitation => invitation.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(invitation => invitation.Inviter)
                .WithMany(user => user.SentTaskInvitations)
                .HasForeignKey(invitation => invitation.InviterId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(invitation => invitation.ClaimedByUser)
                .WithMany(user => user.ClaimedTaskInvitations)
                .HasForeignKey(invitation => invitation.ClaimedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LumaProject>(entity =>
        {
            entity.ToTable("Projects");
            entity.Property(project => project.Name).HasMaxLength(120);
            entity.Property(project => project.Description).HasMaxLength(2000);
            entity.Property(project => project.Version).IsConcurrencyToken();
            entity.HasIndex(project => project.Name);
            entity.HasOne(project => project.CreatedByUser)
                .WithMany(user => user.CreatedProjects)
                .HasForeignKey(project => project.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<InboxItem>(entity =>
        {
            entity.ToTable("InboxItems", table =>
                table.HasCheckConstraint("CK_InboxItems_ActivityType", "[ActivityType] IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9)"));
            entity.Property(item => item.Message).HasMaxLength(500);
            entity.HasIndex(item => new { item.RecipientUserId, item.ReadAt, item.CreatedAt });
            entity.HasIndex(item => item.TaskId);
            entity.HasOne(item => item.Recipient)
                .WithMany(user => user.ReceivedInboxItems)
                .HasForeignKey(item => item.RecipientUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(item => item.Actor)
                .WithMany(user => user.AuthoredInboxItems)
                .HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(item => item.Task)
                .WithMany(task => task.InboxItems)
                .HasForeignKey(item => item.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TaskAttachment>(entity =>
        {
            entity.ToTable("TaskAttachments");
            entity.Property(attachment => attachment.OriginalFileName).HasMaxLength(255);
            entity.Property(attachment => attachment.ContentType).HasMaxLength(100);
            entity.Property(attachment => attachment.StorageKey).HasMaxLength(200);
            entity.HasIndex(attachment => attachment.StorageKey).IsUnique();
            entity.HasIndex(attachment => new { attachment.TaskId, attachment.CreatedAt });
            entity.HasOne(attachment => attachment.Task)
                .WithMany(task => task.Attachments)
                .HasForeignKey(attachment => attachment.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(attachment => attachment.UploadedByUser)
                .WithMany(user => user.UploadedTaskAttachments)
                .HasForeignKey(attachment => attachment.UploadedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TaskMention>(entity =>
        {
            entity.ToTable("TaskMentions");
            entity.HasKey(mention => new { mention.TaskId, mention.UserId });
            entity.HasIndex(mention => mention.UserId);
            entity.HasOne(mention => mention.Task)
                .WithMany(task => task.Mentions)
                .HasForeignKey(mention => mention.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(mention => mention.User)
                .WithMany(user => user.TaskMentions)
                .HasForeignKey(mention => mention.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TaskCommentMention>(entity =>
        {
            entity.ToTable("TaskCommentMentions");
            entity.HasKey(mention => new { mention.CommentId, mention.UserId });
            entity.HasIndex(mention => mention.UserId);
            entity.HasOne(mention => mention.Comment)
                .WithMany(comment => comment.Mentions)
                .HasForeignKey(mention => mention.CommentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(mention => mention.User)
                .WithMany(user => user.TaskCommentMentions)
                .HasForeignKey(mention => mention.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
