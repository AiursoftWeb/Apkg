using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RenameBucketFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AptMirrors_AptBuckets_CurrentBucketId",
                table: "AptMirrors");

            migrationBuilder.DropForeignKey(
                name: "FK_AptRepositories_AptBuckets_CurrentBucketId",
                table: "AptRepositories");

            migrationBuilder.DropForeignKey(
                name: "FK_AptRepositories_AptBuckets_PendingBucketId",
                table: "AptRepositories");

            migrationBuilder.RenameColumn(
                name: "PendingBucketId",
                table: "AptRepositories",
                newName: "SecondaryBucketId");

            migrationBuilder.RenameColumn(
                name: "CurrentBucketId",
                table: "AptRepositories",
                newName: "PrimaryBucketId");

            migrationBuilder.RenameIndex(
                name: "IX_AptRepositories_PendingBucketId",
                table: "AptRepositories",
                newName: "IX_AptRepositories_SecondaryBucketId");

            migrationBuilder.RenameIndex(
                name: "IX_AptRepositories_CurrentBucketId",
                table: "AptRepositories",
                newName: "IX_AptRepositories_PrimaryBucketId");

            migrationBuilder.RenameColumn(
                name: "CurrentBucketId",
                table: "AptMirrors",
                newName: "PrimaryBucketId");

            migrationBuilder.RenameIndex(
                name: "IX_AptMirrors_CurrentBucketId",
                table: "AptMirrors",
                newName: "IX_AptMirrors_PrimaryBucketId");

            migrationBuilder.AddForeignKey(
                name: "FK_AptMirrors_AptBuckets_PrimaryBucketId",
                table: "AptMirrors",
                column: "PrimaryBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AptRepositories_AptBuckets_PrimaryBucketId",
                table: "AptRepositories",
                column: "PrimaryBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AptRepositories_AptBuckets_SecondaryBucketId",
                table: "AptRepositories",
                column: "SecondaryBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AptMirrors_AptBuckets_PrimaryBucketId",
                table: "AptMirrors");

            migrationBuilder.DropForeignKey(
                name: "FK_AptRepositories_AptBuckets_PrimaryBucketId",
                table: "AptRepositories");

            migrationBuilder.DropForeignKey(
                name: "FK_AptRepositories_AptBuckets_SecondaryBucketId",
                table: "AptRepositories");

            migrationBuilder.RenameColumn(
                name: "SecondaryBucketId",
                table: "AptRepositories",
                newName: "PendingBucketId");

            migrationBuilder.RenameColumn(
                name: "PrimaryBucketId",
                table: "AptRepositories",
                newName: "CurrentBucketId");

            migrationBuilder.RenameIndex(
                name: "IX_AptRepositories_SecondaryBucketId",
                table: "AptRepositories",
                newName: "IX_AptRepositories_PendingBucketId");

            migrationBuilder.RenameIndex(
                name: "IX_AptRepositories_PrimaryBucketId",
                table: "AptRepositories",
                newName: "IX_AptRepositories_CurrentBucketId");

            migrationBuilder.RenameColumn(
                name: "PrimaryBucketId",
                table: "AptMirrors",
                newName: "CurrentBucketId");

            migrationBuilder.RenameIndex(
                name: "IX_AptMirrors_PrimaryBucketId",
                table: "AptMirrors",
                newName: "IX_AptMirrors_CurrentBucketId");

            migrationBuilder.AddForeignKey(
                name: "FK_AptMirrors_AptBuckets_CurrentBucketId",
                table: "AptMirrors",
                column: "CurrentBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AptRepositories_AptBuckets_CurrentBucketId",
                table: "AptRepositories",
                column: "CurrentBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AptRepositories_AptBuckets_PendingBucketId",
                table: "AptRepositories",
                column: "PendingBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");
        }
    }
}
