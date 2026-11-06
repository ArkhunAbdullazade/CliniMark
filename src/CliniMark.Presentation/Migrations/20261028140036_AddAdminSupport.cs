using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CliniMark.Presentation.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "MedDatas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StudentLockedAt",
                table: "MedDatas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AnnotatedMedDatas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AnnotatedByStudentsMedDatas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdminUserId = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    MedDataId = table.Column<int>(type: "integer", nullable: true),
                    TargetUserId = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedDatas_LockedByStudentUserId",
                table: "MedDatas",
                column: "LockedByStudentUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MedDatas_LockedByUserId",
                table: "MedDatas",
                column: "LockedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnotatedMedDatas_CreatedAt",
                table: "AnnotatedMedDatas",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnnotatedMedDatas_Email",
                table: "AnnotatedMedDatas",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_AnnotatedMedDatas_MedDataId",
                table: "AnnotatedMedDatas",
                column: "MedDataId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnotatedByStudentsMedDatas_CreatedAt",
                table: "AnnotatedByStudentsMedDatas",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnnotatedByStudentsMedDatas_MedDataId",
                table: "AnnotatedByStudentsMedDatas",
                column: "MedDataId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActions_At",
                table: "AdminActions",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminActions");

            migrationBuilder.DropIndex(
                name: "IX_MedDatas_LockedByStudentUserId",
                table: "MedDatas");

            migrationBuilder.DropIndex(
                name: "IX_MedDatas_LockedByUserId",
                table: "MedDatas");

            migrationBuilder.DropIndex(
                name: "IX_AnnotatedMedDatas_CreatedAt",
                table: "AnnotatedMedDatas");

            migrationBuilder.DropIndex(
                name: "IX_AnnotatedMedDatas_Email",
                table: "AnnotatedMedDatas");

            migrationBuilder.DropIndex(
                name: "IX_AnnotatedMedDatas_MedDataId",
                table: "AnnotatedMedDatas");

            migrationBuilder.DropIndex(
                name: "IX_AnnotatedByStudentsMedDatas_CreatedAt",
                table: "AnnotatedByStudentsMedDatas");

            migrationBuilder.DropIndex(
                name: "IX_AnnotatedByStudentsMedDatas_MedDataId",
                table: "AnnotatedByStudentsMedDatas");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "MedDatas");

            migrationBuilder.DropColumn(
                name: "StudentLockedAt",
                table: "MedDatas");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AnnotatedMedDatas");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AnnotatedByStudentsMedDatas");
        }
    }
}
