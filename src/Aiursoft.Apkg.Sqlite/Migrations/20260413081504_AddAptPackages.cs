using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAptPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AptPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MirrorRepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginSuite = table.Column<string>(type: "TEXT", nullable: false),
                    OriginComponent = table.Column<string>(type: "TEXT", nullable: false),
                    Package = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", nullable: false),
                    Maintainer = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionMd5 = table.Column<string>(type: "TEXT", nullable: false),
                    Section = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    Bugs = table.Column<string>(type: "TEXT", nullable: false),
                    Filename = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<string>(type: "TEXT", nullable: false),
                    MD5sum = table.Column<string>(type: "TEXT", nullable: false),
                    SHA1 = table.Column<string>(type: "TEXT", nullable: false),
                    SHA256 = table.Column<string>(type: "TEXT", nullable: false),
                    SHA512 = table.Column<string>(type: "TEXT", nullable: false),
                    InstalledSize = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalMaintainer = table.Column<string>(type: "TEXT", nullable: true),
                    Homepage = table.Column<string>(type: "TEXT", nullable: true),
                    Depends = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    MultiArch = table.Column<string>(type: "TEXT", nullable: true),
                    Provides = table.Column<string>(type: "TEXT", nullable: true),
                    Suggests = table.Column<string>(type: "TEXT", nullable: true),
                    Recommends = table.Column<string>(type: "TEXT", nullable: true),
                    Conflicts = table.Column<string>(type: "TEXT", nullable: true),
                    Breaks = table.Column<string>(type: "TEXT", nullable: true),
                    Replaces = table.Column<string>(type: "TEXT", nullable: true),
                    Extras = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AptPackages_MirrorRepositories_MirrorRepositoryId",
                        column: x => x.MirrorRepositoryId,
                        principalTable: "MirrorRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_Filename",
                table: "AptPackages",
                column: "Filename");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_MirrorRepositoryId",
                table: "AptPackages",
                column: "MirrorRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_Package_Version_Architecture_OriginSuite_OriginComponent",
                table: "AptPackages",
                columns: new[] { "Package", "Version", "Architecture", "OriginSuite", "OriginComponent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AptPackages");
        }
    }
}
