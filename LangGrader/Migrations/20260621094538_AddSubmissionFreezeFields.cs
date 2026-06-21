using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LangGrader.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionFreezeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FreezeMessage",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreezeStatus",
                table: "Submissions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FrozenAt",
                table: "Submissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelectedForFreeze",
                table: "Submissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreezeMessage",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "FreezeStatus",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "FrozenAt",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "IsSelectedForFreeze",
                table: "Submissions");
        }
    }
}
