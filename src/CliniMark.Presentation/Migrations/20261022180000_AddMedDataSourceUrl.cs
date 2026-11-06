using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    public partial class AddMedDataSourceUrl : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "MedDatas",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SourceUrl", table: "MedDatas");
        }
    }
}
