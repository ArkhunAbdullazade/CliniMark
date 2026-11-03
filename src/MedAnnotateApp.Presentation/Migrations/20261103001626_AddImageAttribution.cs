using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedAnnotateApp.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddImageAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Attribution",
                table: "MedDatas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLicense",
                table: "MedDatas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attribution",
                table: "MedDatas");

            migrationBuilder.DropColumn(
                name: "SourceLicense",
                table: "MedDatas");
        }
    }
}
