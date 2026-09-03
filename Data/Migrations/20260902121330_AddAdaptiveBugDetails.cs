using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdaptiveBugDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BugCategory",
                table: "Tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BugEnvironment",
                table: "Tasks",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BugReproducibility",
                table: "Tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BugSeverity",
                table: "Tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FoundInVersion",
                table: "Tasks",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkItemType",
                table: "Tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "BugReproductionStepId",
                table: "TaskAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BugReproductionSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ObservedResult = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsPrimaryFailure = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BugReproductionSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BugReproductionSteps_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskBugDetails",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedResult = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ObservedResult = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ErrorDetails = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    ExpectedDuration = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualDuration = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Endpoint = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ApiRequest = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    ApiResponse = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DataEntity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DataIdentifier = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExpectedValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ActualValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LastKnownGoodVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    FirstBrokenVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    WorksOn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FailsOn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskBugDetails", x => x.TaskId);
                    table.ForeignKey(
                        name: "FK_TaskBugDetails_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_BugCategory",
                table: "Tasks",
                sql: "[BugCategory] IS NULL OR [BugCategory] IN (0, 1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_BugMetadata",
                table: "Tasks",
                sql: "([WorkItemType] = 0 AND [BugCategory] IS NULL AND [BugSeverity] IS NULL AND [BugReproducibility] IS NULL AND [FoundInVersion] IS NULL AND [BugEnvironment] IS NULL) OR ([WorkItemType] = 1 AND [BugCategory] IS NOT NULL AND [BugSeverity] IS NOT NULL AND [BugReproducibility] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_BugReproducibility",
                table: "Tasks",
                sql: "[BugReproducibility] IS NULL OR [BugReproducibility] IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_BugSeverity",
                table: "Tasks",
                sql: "[BugSeverity] IS NULL OR [BugSeverity] IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_WorkItemType",
                table: "Tasks",
                sql: "[WorkItemType] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttachments_BugReproductionStepId",
                table: "TaskAttachments",
                column: "BugReproductionStepId");

            migrationBuilder.CreateIndex(
                name: "IX_BugReproductionSteps_TaskId_Position",
                table: "BugReproductionSteps",
                columns: new[] { "TaskId", "Position" });

            migrationBuilder.AddForeignKey(
                name: "FK_TaskAttachments_BugReproductionSteps_BugReproductionStepId",
                table: "TaskAttachments",
                column: "BugReproductionStepId",
                principalTable: "BugReproductionSteps",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskAttachments_BugReproductionSteps_BugReproductionStepId",
                table: "TaskAttachments");

            migrationBuilder.DropTable(
                name: "BugReproductionSteps");

            migrationBuilder.DropTable(
                name: "TaskBugDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_BugCategory",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_BugMetadata",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_BugReproducibility",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_BugSeverity",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_WorkItemType",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_TaskAttachments_BugReproductionStepId",
                table: "TaskAttachments");

            migrationBuilder.DropColumn(
                name: "BugCategory",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "BugEnvironment",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "BugReproducibility",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "BugSeverity",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "FoundInVersion",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WorkItemType",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "BugReproductionStepId",
                table: "TaskAttachments");
        }
    }
}
