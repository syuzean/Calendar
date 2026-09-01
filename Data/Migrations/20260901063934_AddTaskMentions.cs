using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskMentions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InboxItems_ActivityType",
                table: "InboxItems");

            migrationBuilder.CreateTable(
                name: "TaskMentions",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Field = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskMentions", x => new { x.TaskId, x.UserId, x.Field });
                    table.CheckConstraint("CK_TaskMentions_Field", "[Field] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_TaskMentions_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskMentions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InboxItems_ActivityType",
                table: "InboxItems",
                sql: "[ActivityType] IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9)");

            migrationBuilder.CreateIndex(
                name: "IX_TaskMentions_UserId",
                table: "TaskMentions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskMentions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InboxItems_ActivityType",
                table: "InboxItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InboxItems_ActivityType",
                table: "InboxItems",
                sql: "[ActivityType] IN (0, 1, 2, 3, 4, 5, 6, 7, 8)");
        }
    }
}
