using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentstoAnnotatedMedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "AnnotatedMedDatas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "AnnotatedMedDatas");
        }
    }
}
