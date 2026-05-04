using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPullResult",
                table: "AptMirrors",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastPullSuccess",
                table: "AptMirrors",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPullTime",
                table: "AptMirrors",
                type: "TEXT",
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
