using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentUsersLocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAnnotatedByStudent",
                table: "MedDatas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LockedByStudentUserId",
                table: "MedDatas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAnnotatedByStudent",
                table: "MedDatas");

            migrationBuilder.DropColumn(
                name: "LockedByStudentUserId",
                table: "MedDatas");
        }
    }
}
