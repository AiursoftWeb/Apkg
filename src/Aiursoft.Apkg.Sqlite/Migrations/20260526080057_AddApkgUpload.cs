using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddApkgUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApkgUploadId",
                table: "LocalPackages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApkgUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Package = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Maintainer = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Homepage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    VaultPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsListed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgUploads_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalPackages_ApkgUploadId",
                table: "LocalPackages",
                column: "ApkgUploadId");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgUploads_UploadedByUserId",
                table: "ApkgUploads",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LocalPackages_ApkgUploads_ApkgUploadId",
                table: "LocalPackages",
                column: "ApkgUploadId",
                principalTable: "ApkgUploads",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocalPackages_ApkgUploads_ApkgUploadId",
                table: "LocalPackages");

            migrationBuilder.DropTable(
                name: "ApkgUploads");

            migrationBuilder.DropIndex(
                name: "IX_LocalPackages_ApkgUploadId",
                table: "LocalPackages");

            migrationBuilder.DropColumn(
                name: "ApkgUploadId",
                table: "LocalPackages");
        }
    }
}
