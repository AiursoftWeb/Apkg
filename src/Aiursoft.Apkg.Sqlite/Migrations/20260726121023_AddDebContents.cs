using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDebContents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DebContents",
                columns: table => new
                {
                    SHA256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebContents", x => x.SHA256);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DebContents_CreatedAt",
                table: "DebContents",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebContents");
        }
    }
}
