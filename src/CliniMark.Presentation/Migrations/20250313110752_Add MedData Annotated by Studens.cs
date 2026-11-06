using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddMedDataAnnotatedbyStudens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsThirdStageAnnotated",
                table: "MedDatas");

            migrationBuilder.CreateTable(
                name: "AnnotatedByStudentsMedDatas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MedDataId = table.Column<int>(type: "integer", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    ImageDescription = table.Column<string>(type: "text", nullable: true),
                    Sex = table.Column<string>(type: "text", nullable: true),
                    Age = table.Column<string>(type: "text", nullable: true),
                    SkinTone = table.Column<string>(type: "text", nullable: true),
                    BodyRegion = table.Column<string>(type: "text", nullable: true),
                    Diagnosis = table.Column<string>(type: "text", nullable: true),
                    TreatmentName = table.Column<string>(type: "text", nullable: true),
                    Speciality = table.Column<string>(type: "text", nullable: true),
                    Modality = table.Column<string>(type: "text", nullable: true),
                    BoxesCoordinates = table.Column<string>(type: "text", nullable: true),
                    ExtractedKeyword = table.Column<string>(type: "text", nullable: true),
                    PressedButton = table.Column<string>(type: "text", nullable: true),
                    Timestamps = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    FullName = table.Column<string>(type: "text", nullable: true),
                    University = table.Column<string>(type: "text", nullable: true),
                    Position = table.Column<string>(type: "text", nullable: true),
                    ClinicalExperience = table.Column<int>(type: "integer", nullable: false),
                    OrcidId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnotatedByStudentsMedDatas", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnnotatedByStudentsMedDatas");

            migrationBuilder.AddColumn<bool>(
                name: "IsThirdStageAnnotated",
                table: "MedDatas",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
