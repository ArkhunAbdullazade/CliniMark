using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetadataChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MedDataId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Field = table.Column<string>(type: "text", nullable: false),
                    ShownValue = table.Column<string>(type: "text", nullable: true),
                    Verdict = table.Column<string>(type: "text", nullable: false),
                    CorrectedValue = table.Column<string>(type: "text", nullable: true),
                    IsNewValue = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataChecks_IsNewValue",
                table: "MetadataChecks",
                column: "IsNewValue");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataChecks_MedDataId_UserId_Field",
                table: "MetadataChecks",
                columns: new[] { "MedDataId", "UserId", "Field" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetadataChecks");
        }
    }
}
