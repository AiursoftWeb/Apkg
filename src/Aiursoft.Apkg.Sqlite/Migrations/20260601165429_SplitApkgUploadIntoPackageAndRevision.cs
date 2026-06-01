using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SplitApkgUploadIntoPackageAndRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocalPackages_ApkgUploads_ApkgUploadId",
                table: "LocalPackages");

            migrationBuilder.DropTable(
                name: "ApkgUploads");

            migrationBuilder.RenameColumn(
                name: "ApkgUploadId",
                table: "LocalPackages",
                newName: "ApkgRevisionId");

            migrationBuilder.RenameIndex(
                name: "IX_LocalPackages_ApkgUploadId",
                table: "LocalPackages",
                newName: "IX_LocalPackages_ApkgRevisionId");

            migrationBuilder.CreateTable(
                name: "ApkgPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Distro = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Maintainer = table.Column<string>(type: "TEXT", nullable: true),
                    Homepage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    License = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OwnerUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgPackages_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApkgRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApkgPackageId = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    VaultPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsListed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgRevisions_ApkgPackages_ApkgPackageId",
                        column: x => x.ApkgPackageId,
                        principalTable: "ApkgPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApkgRevisions_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApkgPackages_Name_Distro_Component",
                table: "ApkgPackages",
                columns: new[] { "Name", "Distro", "Component" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApkgPackages_OwnerUserId",
                table: "ApkgPackages",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgRevisions_ApkgPackageId",
                table: "ApkgRevisions",
                column: "ApkgPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgRevisions_UploadedByUserId",
                table: "ApkgRevisions",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LocalPackages_ApkgRevisions_ApkgRevisionId",
                table: "LocalPackages",
                column: "ApkgRevisionId",
                principalTable: "ApkgRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocalPackages_ApkgRevisions_ApkgRevisionId",
                table: "LocalPackages");

            migrationBuilder.DropTable(
                name: "ApkgRevisions");

            migrationBuilder.DropTable(
                name: "ApkgPackages");

            migrationBuilder.RenameColumn(
                name: "ApkgRevisionId",
                table: "LocalPackages",
                newName: "ApkgUploadId");

            migrationBuilder.RenameIndex(
                name: "IX_LocalPackages_ApkgRevisionId",
                table: "LocalPackages",
                newName: "IX_LocalPackages_ApkgUploadId");

            migrationBuilder.CreateTable(
                name: "ApkgUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Distro = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Homepage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsListed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    Maintainer = table.Column<string>(type: "TEXT", nullable: true),
                    Package = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VaultPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
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
    }
}
