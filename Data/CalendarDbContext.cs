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
            entity.Property(task => task.Description).HasMaxLength(4000);
            entity.Property(task => task.DeadlineChangeComment).HasMaxLength(1000);
            entity.Property(task => task.Deadline).HasColumnType("date");
            entity.Property(task => task.RequestedDeadline).HasColumnType("date");
            entity.Property(task => task.Version).IsConcurrencyToken();
            entity.ToTable("Tasks", table =>
            {
                table.HasCheckConstraint("CK_Tasks_AssignmentStatus", "[AssignmentStatus] IN (0, 1, 2)");
                table.HasCheckConstraint("CK_Tasks_WorkStatus", "[WorkStatus] IN (0, 1, 2)");
            });
            entity.HasOne(task => task.Creator)
                .WithMany(user => user.CreatedTasks)
                .HasForeignKey(task => task.CreatorId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(task => task.Assignee)
                .WithMany(user => user.AssignedTasks)
                .HasForeignKey(task => task.AssigneeId)
                .OnDelete(DeleteBehavior.NoAction);
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
    }
}
