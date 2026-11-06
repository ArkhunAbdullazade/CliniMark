using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class ChangeAnnotatedByStudentsMedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoxesCoordinates",
                table: "AnnotatedByStudentsMedDatas");

            migrationBuilder.DropColumn(
                name: "ExtractedKeyword",
                table: "AnnotatedByStudentsMedDatas");

            migrationBuilder.RenameColumn(
                name: "Timestamps",
                table: "AnnotatedByStudentsMedDatas",
                newName: "TextualAnnotation");

            migrationBuilder.RenameColumn(
                name: "PressedButton",
                table: "AnnotatedByStudentsMedDatas",
                newName: "Coordinates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TextualAnnotation",
                table: "AnnotatedByStudentsMedDatas",
                newName: "Timestamps");

            migrationBuilder.RenameColumn(
                name: "Coordinates",
                table: "AnnotatedByStudentsMedDatas",
                newName: "PressedButton");

            migrationBuilder.AddColumn<string>(
                name: "BoxesCoordinates",
                table: "AnnotatedByStudentsMedDatas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedKeyword",
                table: "AnnotatedByStudentsMedDatas",
                type: "text",
                nullable: true);
        }
    }
}
