using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddMirrorSecondaryBucketId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SecondaryBucketId",
                table: "AptMirrors",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AptMirrors_SecondaryBucketId",
                table: "AptMirrors",
                column: "SecondaryBucketId");

            migrationBuilder.AddForeignKey(
                name: "FK_AptMirrors_AptBuckets_SecondaryBucketId",
                table: "AptMirrors",
                column: "SecondaryBucketId",
                principalTable: "AptBuckets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AptMirrors_AptBuckets_SecondaryBucketId",
                table: "AptMirrors");

            migrationBuilder.DropIndex(
                name: "IX_AptMirrors_SecondaryBucketId",
                table: "AptMirrors");

            migrationBuilder.DropColumn(
                name: "SecondaryBucketId",
                table: "AptMirrors");
        }
    }
}
