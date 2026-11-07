using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorialWatchedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TutorialWatchedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TutorialWatchedAt",
                table: "AspNetUsers");
        }
    }
}
