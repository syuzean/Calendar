using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationRsvpResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponseComment",
                table: "EventInvitations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseUtc",
                table: "EventInvitations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [EventInvitations]
                SET [Status] = 0
                WHERE [Status] = 1;

                INSERT INTO [EventInvitations] (
                    [Id], [EventId], [InvitedEmail], [NormalizedEmail], [InvitedUserId],
                    [Status], [TokenHash], [CreatedUtc], [ExpiresUtc], [RespondedUtc],
                    [ResponseComment], [ResponseUtc], [EmailStatus])
                SELECT
                    NEWID(), participant.[EventId], invitedUser.[Email], invitedUser.[NormalizedEmail], participant.[UserId],
                    0,
                    CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(nvarchar(36), NEWID())), 2),
                    SYSUTCDATETIME(), DATEADD(day, 14, SYSUTCDATETIME()), SYSUTCDATETIME(),
                    N'', NULL, 0
                FROM [EventParticipants] AS participant
                INNER JOIN [Users] AS invitedUser ON invitedUser.[Id] = participant.[UserId]
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [EventInvitations] AS invitation
                    WHERE invitation.[EventId] = participant.[EventId]
                      AND invitation.[NormalizedEmail] = invitedUser.[NormalizedEmail]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseComment",
                table: "EventInvitations");

            migrationBuilder.DropColumn(
                name: "ResponseUtc",
                table: "EventInvitations");
        }
    }
}
