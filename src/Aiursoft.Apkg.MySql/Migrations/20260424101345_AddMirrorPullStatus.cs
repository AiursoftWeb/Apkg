using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddMirrorPullStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastPullErrorStack",
                table: "AptMirrors",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LastPullResult",
                table: "AptMirrors",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "LastPullSuccess",
                table: "AptMirrors",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPullTime",
                table: "AptMirrors",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPullErrorStack",
                table: "AptMirrors");

            migrationBuilder.DropColumn(
                name: "LastPullResult",
                table: "AptMirrors");

            migrationBuilder.DropColumn(
                name: "LastPullSuccess",
                table: "AptMirrors");

            migrationBuilder.DropColumn(
                name: "LastPullTime",
                table: "AptMirrors");
        }
    }
}
