using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class PipelineV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AptPackages_MirrorRepositories_MirrorRepositoryId",
                table: "AptPackages");

            migrationBuilder.DropTable(
                name: "MirrorRepositories");

            migrationBuilder.DropIndex(
                name: "IX_AptPackages_MirrorRepositoryId",
                table: "AptPackages");

            migrationBuilder.DropIndex(
                name: "IX_AptPackages_Package_Version_Architecture_OriginSuite_OriginComponent",
                table: "AptPackages");

            migrationBuilder.RenameColumn(
                name: "MirrorRepositoryId",
                table: "AptPackages",
                newName: "IsVirtual");

            migrationBuilder.AddColumn<int>(
                name: "BucketId",
                table: "AptPackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Component",
                table: "AptPackages",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemoteUrl",
                table: "AptPackages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AptBuckets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InReleaseContent = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseContent = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptBuckets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AptMirrors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Suite = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Components = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SignedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentBucketId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptMirrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AptMirrors_AptBuckets_CurrentBucketId",
                        column: x => x.CurrentBucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AptRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Suite = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CertificateId = table.Column<int>(type: "INTEGER", nullable: true),
                    MirrorId = table.Column<int>(type: "INTEGER", nullable: true),
                    CurrentBucketId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AptRepositories_AptBuckets_CurrentBucketId",
                        column: x => x.CurrentBucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AptRepositories_AptCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "AptCertificates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AptRepositories_AptMirrors_MirrorId",
                        column: x => x.MirrorId,
                        principalTable: "AptMirrors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_BucketId",
                table: "AptPackages",
                column: "BucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_Package_Version_Architecture_Component",
                table: "AptPackages",
                columns: new[] { "Package", "Version", "Architecture", "Component" });

            migrationBuilder.CreateIndex(
                name: "IX_AptMirrors_CurrentBucketId",
                table: "AptMirrors",
                column: "CurrentBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_CertificateId",
                table: "AptRepositories",
                column: "CertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_CurrentBucketId",
                table: "AptRepositories",
                column: "CurrentBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_MirrorId",
                table: "AptRepositories",
                column: "MirrorId");

            migrationBuilder.AddForeignKey(
                name: "FK_AptPackages_AptBuckets_BucketId",
                table: "AptPackages",
                column: "BucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AptPackages_AptBuckets_BucketId",
                table: "AptPackages");

            migrationBuilder.DropTable(
                name: "AptRepositories");

            migrationBuilder.DropTable(
                name: "AptMirrors");

            migrationBuilder.DropTable(
                name: "AptBuckets");

            migrationBuilder.DropIndex(
                name: "IX_AptPackages_BucketId",
                table: "AptPackages");

            migrationBuilder.DropIndex(
                name: "IX_AptPackages_Package_Version_Architecture_Component",
                table: "AptPackages");

            migrationBuilder.DropColumn(
                name: "BucketId",
                table: "AptPackages");

            migrationBuilder.DropColumn(
                name: "Component",
                table: "AptPackages");

            migrationBuilder.DropColumn(
                name: "RemoteUrl",
                table: "AptPackages");

            migrationBuilder.RenameColumn(
                name: "IsVirtual",
                table: "AptPackages",
                newName: "MirrorRepositoryId");

            migrationBuilder.CreateTable(
                name: "MirrorRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CertificateId = table.Column<int>(type: "INTEGER", nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SignedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Suite = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MirrorRepositories_AptCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "AptCertificates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_MirrorRepositoryId",
                table: "AptPackages",
                column: "MirrorRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_Package_Version_Architecture_OriginSuite_OriginComponent",
                table: "AptPackages",
                columns: new[] { "Package", "Version", "Architecture", "OriginSuite", "OriginComponent" });

            migrationBuilder.CreateIndex(
                name: "IX_MirrorRepositories_CertificateId",
                table: "MirrorRepositories",
                column: "CertificateId");

            migrationBuilder.AddForeignKey(
                name: "FK_AptPackages_MirrorRepositories_MirrorRepositoryId",
                table: "AptPackages",
                column: "MirrorRepositoryId",
                principalTable: "MirrorRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
