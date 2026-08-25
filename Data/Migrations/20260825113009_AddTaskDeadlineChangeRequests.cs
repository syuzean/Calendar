using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskDeadlineChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_AssignmentStatus",
                table: "Tasks");

            migrationBuilder.AddColumn<string>(
                name: "DeadlineChangeComment",
                table: "Tasks",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadlineChangeRequestedAt",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedDeadline",
                table: "Tasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_AssignmentStatus",
                table: "Tasks",
                sql: "[AssignmentStatus] IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_AssignmentStatus",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DeadlineChangeComment",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DeadlineChangeRequestedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RequestedDeadline",
                table: "Tasks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_AssignmentStatus",
                table: "Tasks",
                sql: "[AssignmentStatus] IN (0, 1)");
        }
    }
}
