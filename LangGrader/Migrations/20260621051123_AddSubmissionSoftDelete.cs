using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LangGrader.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Submissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Submissions");
        }
    }
}
