using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuildFinished : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildFinished",
                table: "AptBuckets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BuildFinished",
                table: "AptBuckets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
