using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.MySql.Migrations
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
                name: "IX_AptPackages_Package_Version_Architecture_OriginSuite_OriginC~",
                table: "AptPackages");

            migrationBuilder.RenameColumn(
                name: "MirrorRepositoryId",
                table: "AptPackages",
                newName: "BucketId");

            migrationBuilder.RenameIndex(
                name: "IX_AptPackages_MirrorRepositoryId",
                table: "AptPackages",
                newName: "IX_AptPackages_BucketId");

            migrationBuilder.AddColumn<string>(
                name: "Component",
                table: "AptPackages",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsVirtual",
                table: "AptPackages",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RemoteUrl",
                table: "AptPackages",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AptBuckets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    InReleaseContent = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReleaseContent = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptBuckets", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AptMirrors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BaseUrl = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Suite = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Components = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Architecture = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentBucketId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptMirrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AptMirrors_AptBuckets_CurrentBucketId",
                        column: x => x.CurrentBucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AptRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Suite = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CertificateId = table.Column<int>(type: "int", nullable: true),
                    MirrorId = table.Column<int>(type: "int", nullable: true),
                    CurrentBucketId = table.Column<int>(type: "int", nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
                name: "IX_AptPackages_Package_Version_Architecture_Component",
                table: "AptPackages");

            migrationBuilder.DropColumn(
                name: "Component",
                table: "AptPackages");

            migrationBuilder.DropColumn(
                name: "IsVirtual",
                table: "AptPackages");

            migrationBuilder.DropColumn(
                name: "RemoteUrl",
                table: "AptPackages");

            migrationBuilder.RenameColumn(
                name: "BucketId",
                table: "AptPackages",
                newName: "MirrorRepositoryId");

            migrationBuilder.RenameIndex(
                name: "IX_AptPackages_BucketId",
                table: "AptPackages",
                newName: "IX_AptPackages_MirrorRepositoryId");

            migrationBuilder.CreateTable(
                name: "MirrorRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CertificateId = table.Column<int>(type: "int", nullable: true),
                    Architecture = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BaseUrl = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Component = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Suite = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MirrorRepositories_AptCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "AptCertificates",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_Package_Version_Architecture_OriginSuite_OriginC~",
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
