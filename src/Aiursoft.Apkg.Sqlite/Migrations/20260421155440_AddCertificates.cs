using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
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
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AptCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    PrivateKey = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptCertificates", x => x.Id);
                });

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
