using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LangGrader.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentAutoFreeze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoFreezeDelayMinutes",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutoFreezeEnabled",
                table: "Assignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoFreezeDelayMinutes",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "AutoFreezeEnabled",
                table: "Assignments");
        }
    }
}
