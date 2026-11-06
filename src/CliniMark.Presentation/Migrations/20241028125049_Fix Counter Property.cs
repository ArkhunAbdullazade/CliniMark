using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class FixCounterProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Specialty",
                table: "MaxCounters",
                newName: "Speciality");

            migrationBuilder.RenameColumn(
                name: "Specialty",
                table: "CurrentCounters",
                newName: "Speciality");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Speciality",
                table: "MaxCounters",
                newName: "Specialty");

            migrationBuilder.RenameColumn(
                name: "Speciality",
                table: "CurrentCounters",
                newName: "Specialty");
        }
    }
}
