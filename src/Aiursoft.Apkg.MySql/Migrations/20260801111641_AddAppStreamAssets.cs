using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddAppStreamAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApkgAppStreamApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ApkgRevisionId = table.Column<int>(type: "int", nullable: false),
                    ComponentId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DesktopId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetainfoPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgAppStreamApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgAppStreamApplications_ApkgRevisions_ApkgRevisionId",
                        column: x => x.ApkgRevisionId,
                        principalTable: "ApkgRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApkgAppStreamAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ApkgAppStreamApplicationId = table.Column<int>(type: "int", nullable: false),
                    SourceSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ObjectSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MediaType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Locale = table.Column<string>(type: "varchar(35)", maxLength: 35, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Environment = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Caption = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgAppStreamAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgAppStreamAssets_ApkgAppStreamApplications_ApkgAppStreamA~",
                        column: x => x.ApkgAppStreamApplicationId,
                        principalTable: "ApkgAppStreamApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgAppStreamApplications_ApkgRevisionId_ComponentId",
                table: "ApkgAppStreamApplications",
                columns: new[] { "ApkgRevisionId", "ComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApkgAppStreamAssets_ApkgAppStreamApplicationId_Order",
                table: "ApkgAppStreamAssets",
                columns: new[] { "ApkgAppStreamApplicationId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApkgAppStreamAssets_ObjectSha256",
                table: "ApkgAppStreamAssets",
                column: "ObjectSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApkgAppStreamAssets");

            migrationBuilder.DropTable(
                name: "ApkgAppStreamApplications");
        }
    }
}
