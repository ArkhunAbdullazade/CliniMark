using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddKeywordStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeywordStates",
                table: "MedDatas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeywordStates",
                table: "MedDatas");
        }
    }
}
