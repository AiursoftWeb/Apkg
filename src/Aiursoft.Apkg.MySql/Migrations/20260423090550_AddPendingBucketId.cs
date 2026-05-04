using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingBucketId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PendingBucketId",
                table: "AptRepositories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_PendingBucketId",
                table: "AptRepositories",
                column: "PendingBucketId");

            migrationBuilder.AddForeignKey(
                name: "FK_AptRepositories_AptBuckets_PendingBucketId",
                table: "AptRepositories",
                column: "PendingBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AptRepositories_AptBuckets_PendingBucketId",
                table: "AptRepositories");

            migrationBuilder.DropIndex(
                name: "IX_AptRepositories_PendingBucketId",
                table: "AptRepositories");

            migrationBuilder.DropColumn(
                name: "PendingBucketId",
                table: "AptRepositories");
        }
    }
}
