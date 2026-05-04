using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAnyoneToUpload",
                table: "AptRepositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LocalPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Package = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Maintainer = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Section = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<string>(type: "TEXT", nullable: true),
                    Homepage = table.Column<string>(type: "TEXT", nullable: true),
                    InstalledSize = table.Column<string>(type: "TEXT", nullable: true),
                    Depends = table.Column<string>(type: "TEXT", nullable: true),
                    Recommends = table.Column<string>(type: "TEXT", nullable: true),
                    Suggests = table.Column<string>(type: "TEXT", nullable: true),
                    Conflicts = table.Column<string>(type: "TEXT", nullable: true),
                    Breaks = table.Column<string>(type: "TEXT", nullable: true),
                    Replaces = table.Column<string>(type: "TEXT", nullable: true),
                    Provides = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    MultiArch = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalMaintainer = table.Column<string>(type: "TEXT", nullable: true),
                    Filename = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<string>(type: "TEXT", nullable: false),
                    SHA256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MD5sum = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SHA1 = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SHA512 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalPackages_AptRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "AptRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocalPackages_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalPackages_RepositoryId_Package_Architecture",
                table: "LocalPackages",
                columns: new[] { "RepositoryId", "Package", "Architecture" });

            migrationBuilder.CreateIndex(
                name: "IX_LocalPackages_UploadedByUserId",
                table: "LocalPackages",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalPackages");

            migrationBuilder.DropColumn(
                name: "AllowAnyoneToUpload",
                table: "AptRepositories");
        }
    }
}
