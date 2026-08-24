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
    }
}
