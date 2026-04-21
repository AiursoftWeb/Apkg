using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CertificateId",
                table: "MirrorRepositories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AptCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FriendlyName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublicKey = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrivateKey = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fingerprint = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptCertificates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorRepositories_CertificateId",
                table: "MirrorRepositories",
                column: "CertificateId");

            migrationBuilder.AddForeignKey(
                name: "FK_MirrorRepositories_AptCertificates_CertificateId",
                table: "MirrorRepositories",
                column: "CertificateId",
                principalTable: "AptCertificates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MirrorRepositories_AptCertificates_CertificateId",
                table: "MirrorRepositories");

            migrationBuilder.DropTable(
                name: "AptCertificates");

            migrationBuilder.DropIndex(
                name: "IX_MirrorRepositories_CertificateId",
                table: "MirrorRepositories");

            migrationBuilder.DropColumn(
                name: "CertificateId",
                table: "MirrorRepositories");
        }
    }
}
