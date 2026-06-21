using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LangGrader.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentFreezeRootPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FreezeRootPath",
                table: "Assignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FrozenAt",
                table: "Assignments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreezeRootPath",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "FrozenAt",
                table: "Assignments");
        }
    }
}
