using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnswerRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AnswerId = table.Column<int>(type: "integer", nullable: false),
                    MedDataId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ExtractedKeyword = table.Column<string>(type: "text", nullable: true),
                    PressedButton = table.Column<string>(type: "text", nullable: true),
                    BoxCoordinates = table.Column<string>(type: "text", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    SavedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevisedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnswerRevisions_AnnotatedMedDatas_AnswerId",
                        column: x => x.AnswerId,
                        principalTable: "AnnotatedMedDatas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerRevisions_AnswerId",
                table: "AnswerRevisions",
                column: "AnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerRevisions_MedDataId",
                table: "AnswerRevisions",
                column: "MedDataId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerRevisions_RevisedAt",
                table: "AnswerRevisions",
                column: "RevisedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnswerRevisions");
        }
    }
}
