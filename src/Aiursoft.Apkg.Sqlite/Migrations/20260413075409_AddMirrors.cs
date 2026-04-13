using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMirrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MirrorRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Suite = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Components = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    SignedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorRepositories", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MirrorRepositories");
        }
    }
}
