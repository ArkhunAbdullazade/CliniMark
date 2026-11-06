using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MedAnnotateApp.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddImageSkipsAndPixelMasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PixelMask",
                table: "CustomAnnotations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixelMask",
                table: "AnnotatedMedDatas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PixelMask",
                table: "AnnotatedByStudentsMedDatas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImageSkips",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MedDataId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DiscardedWork = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageSkips", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImageSkips_MedDataId_UserId",
                table: "ImageSkips",
                columns: new[] { "MedDataId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImageSkips");

            migrationBuilder.DropColumn(
                name: "PixelMask",
                table: "CustomAnnotations");

            migrationBuilder.DropColumn(
                name: "PixelMask",
                table: "AnnotatedMedDatas");

            migrationBuilder.DropColumn(
                name: "PixelMask",
                table: "AnnotatedByStudentsMedDatas");
        }
    }
}
