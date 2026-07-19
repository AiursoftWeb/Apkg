using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RestrictApkgPackageDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApkgPackages_AspNetUsers_OwnerUserId",
                table: "ApkgPackages");

            migrationBuilder.AddForeignKey(
                name: "FK_ApkgPackages_AspNetUsers_OwnerUserId",
                table: "ApkgPackages",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApkgPackages_AspNetUsers_OwnerUserId",
                table: "ApkgPackages");

            migrationBuilder.AddForeignKey(
                name: "FK_ApkgPackages_AspNetUsers_OwnerUserId",
                table: "ApkgPackages",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
