using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calendar.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredTaskDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Tasks",
                newName: "Problem");

            migrationBuilder.AddColumn<string>(
                name: "ExpectedResult",
                table: "Tasks",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedResult",
                table: "Tasks");

            migrationBuilder.RenameColumn(
                name: "Problem",
                table: "Tasks",
                newName: "Description");
        }
    }
}
