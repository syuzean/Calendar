using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventInvitationsV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[EventInvitations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [EventInvitations] (
                        [Id] uniqueidentifier NOT NULL,
                        [EventId] uniqueidentifier NOT NULL,
                        [InvitedEmail] nvarchar(254) NOT NULL,
                        [NormalizedEmail] nvarchar(254) NOT NULL,
                        [InvitedUserId] uniqueidentifier NULL,
                        [Status] int NOT NULL,
                        [TokenHash] nvarchar(64) NOT NULL,
                        [CreatedUtc] datetime2 NOT NULL,
                        [ExpiresUtc] datetime2 NOT NULL,
                        [RespondedUtc] datetime2 NULL,
                        [RowVersion] rowversion NOT NULL,
                        [EmailLastError] nvarchar(1000) NULL,
                        [EmailSentUtc] datetime2 NULL,
                        [EmailStatus] int NOT NULL CONSTRAINT [DF_EventInvitations_EmailStatus] DEFAULT (0),
                        CONSTRAINT [PK_EventInvitations] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_EventInvitations_Events_EventId] FOREIGN KEY ([EventId]) REFERENCES [Events] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_EventInvitations_Users_InvitedUserId] FOREIGN KEY ([InvitedUserId]) REFERENCES [Users] ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_EventInvitations_EventId_NormalizedEmail]
                        ON [EventInvitations] ([EventId], [NormalizedEmail]);
                    CREATE INDEX [IX_EventInvitations_InvitedUserId_Status]
                        ON [EventInvitations] ([InvitedUserId], [Status]);
                    CREATE UNIQUE INDEX [IX_EventInvitations_TokenHash]
                        ON [EventInvitations] ([TokenHash]);
                    EXEC sys.sp_addextendedproperty
                        @name=N'LumaMigrationOwner', @value=N'AddEventInvitationsV1',
                        @level0type=N'SCHEMA', @level0name=N'dbo',
                        @level1type=N'TABLE', @level1name=N'EventInvitations';
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.extended_properties
                    WHERE [major_id] = OBJECT_ID(N'[dbo].[EventInvitations]')
                      AND [minor_id] = 0
                      AND [name] = N'LumaMigrationOwner'
                      AND CONVERT(nvarchar(100), [value]) = N'AddEventInvitationsV1')
                    DROP TABLE [EventInvitations];
                """);
        }
    }
}
