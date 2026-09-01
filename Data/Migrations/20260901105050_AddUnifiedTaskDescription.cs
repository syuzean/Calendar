using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedTaskDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TaskMentions",
                table: "TaskMentions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskMentions_Field",
                table: "TaskMentions");

            migrationBuilder.RenameColumn(
                name: "Problem",
                table: "Tasks",
                newName: "Description");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "nvarchar(max)",
                maxLength: 10000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000);

            migrationBuilder.Sql(
                """
                UPDATE [Tasks]
                SET [Description] = CASE
                    WHEN NULLIF(LTRIM(RTRIM([ExpectedResult])), '') IS NULL THEN [Description]
                    WHEN NULLIF(LTRIM(RTRIM([Description])), '') IS NULL
                        THEN '## Expected Result' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + [ExpectedResult]
                    ELSE [Description] + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10)
                        + '## Expected Result' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + [ExpectedResult]
                END;
                """);

            migrationBuilder.Sql(
                """
                DELETE duplicateMention
                FROM [TaskMentions] AS duplicateMention
                WHERE duplicateMention.[Field] = 1
                  AND EXISTS (
                      SELECT 1
                      FROM [TaskMentions] AS existingMention
                      WHERE existingMention.[TaskId] = duplicateMention.[TaskId]
                        AND existingMention.[UserId] = duplicateMention.[UserId]
                        AND existingMention.[Field] = 0);

                UPDATE [TaskMentions]
                SET [Field] = 0
                WHERE [Field] <> 0;
                """);

            migrationBuilder.DropColumn(
                name: "ExpectedResult",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Field",
                table: "TaskMentions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TaskMentions",
                table: "TaskMentions",
                columns: new[] { "TaskId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TaskMentions",
                table: "TaskMentions");

            migrationBuilder.AddColumn<string>(
                name: "ExpectedResult",
                table: "Tasks",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Tasks",
                newName: "Problem");

            migrationBuilder.AlterColumn<string>(
                name: "Problem",
                table: "Tasks",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 10000);

            migrationBuilder.AddColumn<int>(
                name: "Field",
                table: "TaskMentions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TaskMentions",
                table: "TaskMentions",
                columns: new[] { "TaskId", "UserId", "Field" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskMentions_Field",
                table: "TaskMentions",
                sql: "[Field] IN (0, 1)");
        }
    }
}
