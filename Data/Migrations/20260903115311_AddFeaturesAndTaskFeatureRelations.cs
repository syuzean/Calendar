using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeaturesAndTaskFeatureRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskChangeLogs_ChangeType",
                table: "TaskChangeLogs");

            migrationBuilder.CreateTable(
                name: "Features",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Features", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Features_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Features_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TaskFeatures",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeatureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskFeatures", x => new { x.TaskId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_TaskFeatures_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskFeatures_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskChangeLogs_ChangeType",
                table: "TaskChangeLogs",
                sql: "[ChangeType] IN (0, 1, 2, 3, 4, 5, 6, 7, 8)");

            migrationBuilder.CreateIndex(
                name: "IX_Features_CreatedByUserId",
                table: "Features",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Features_ProjectId_NormalizedName",
                table: "Features",
                columns: new[] { "ProjectId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskFeatures_FeatureId",
                table: "TaskFeatures",
                column: "FeatureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskFeatures");

            migrationBuilder.DropTable(
                name: "Features");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskChangeLogs_ChangeType",
                table: "TaskChangeLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskChangeLogs_ChangeType",
                table: "TaskChangeLogs",
                sql: "[ChangeType] IN (0, 1, 2, 3, 4, 5, 6)");
        }
    }
}
