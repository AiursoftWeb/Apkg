using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApkgRevisionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DesktopId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MetainfoPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ApkgAppStreamAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApkgAppStreamApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ObjectSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 35, nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgAppStreamAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgAppStreamAssets_ApkgAppStreamApplications_ApkgAppStreamApplicationId",
                        column: x => x.ApkgAppStreamApplicationId,
                        principalTable: "ApkgAppStreamApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
