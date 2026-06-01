using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.Apkg.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AptBuckets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InReleaseContent = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseContent = table.Column<string>(type: "TEXT", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptBuckets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AptCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FriendlyName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    PrivateKey = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptCertificates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AvatarRelativePath = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AptMirrors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Distro = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Suite = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Components = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SignedBy = table.Column<string>(type: "TEXT", nullable: true),
                    AllowInsecure = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrimaryBucketId = table.Column<int>(type: "INTEGER", nullable: true),
                    SecondaryBucketId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastPullTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastPullSuccess = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastPullResult = table.Column<string>(type: "TEXT", nullable: true),
                    LastPullErrorStack = table.Column<string>(type: "TEXT", nullable: true),
                    LastVerifyLog = table.Column<string>(type: "TEXT", nullable: true),
                    LastContentHash = table.Column<string>(type: "TEXT", nullable: true),
                    LastPrimaryReplacedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptMirrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AptMirrors_AptBuckets_PrimaryBucketId",
                        column: x => x.PrimaryBucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AptMirrors_AptBuckets_SecondaryBucketId",
                        column: x => x.SecondaryBucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AptPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BucketId = table.Column<int>(type: "INTEGER", nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsVirtual = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemoteUrl = table.Column<string>(type: "TEXT", nullable: true),
                    OriginSuite = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OriginComponent = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Package = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
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
                        name: "FK_AptPackages_AptBuckets_BucketId",
                        column: x => x.BucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApkgPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Distro = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Component = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Maintainer = table.Column<string>(type: "TEXT", nullable: true),
                    Homepage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    License = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OwnerUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgPackages_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserApiKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AptRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Distro = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Suite = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Components = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CertificateId = table.Column<int>(type: "INTEGER", nullable: true),
                    EnableGpgSign = table.Column<bool>(type: "INTEGER", nullable: false),
                    MirrorId = table.Column<int>(type: "INTEGER", nullable: true),
                    PrimaryBucketId = table.Column<int>(type: "INTEGER", nullable: true),
                    SecondaryBucketId = table.Column<int>(type: "INTEGER", nullable: true),
                    AllowAnyoneToUpload = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AptRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AptRepositories_AptBuckets_PrimaryBucketId",
                        column: x => x.PrimaryBucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AptRepositories_AptBuckets_SecondaryBucketId",
                        column: x => x.SecondaryBucketId,
                        principalTable: "AptBuckets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AptRepositories_AptCertificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "AptCertificates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AptRepositories_AptMirrors_MirrorId",
                        column: x => x.MirrorId,
                        principalTable: "AptMirrors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApkgRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApkgPackageId = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TempApkgFileInVaultPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsListed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgRevisions_ApkgPackages_ApkgPackageId",
                        column: x => x.ApkgPackageId,
                        principalTable: "ApkgPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApkgRevisions_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DependencyCheckReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpireAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalPackages = table.Column<int>(type: "INTEGER", nullable: false),
                    ProblematicPackages = table.Column<int>(type: "INTEGER", nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyCheckReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DependencyCheckReports_AptRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "AptRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApkgDebPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ApkgRevisionId = table.Column<int>(type: "INTEGER", nullable: true),
                    RepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Package = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Architecture = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Maintainer = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Section = table.Column<string>(type: "TEXT", nullable: true),
                    Priority = table.Column<string>(type: "TEXT", nullable: true),
                    Homepage = table.Column<string>(type: "TEXT", nullable: true),
                    InstalledSize = table.Column<string>(type: "TEXT", nullable: true),
                    Depends = table.Column<string>(type: "TEXT", nullable: true),
                    Recommends = table.Column<string>(type: "TEXT", nullable: true),
                    Suggests = table.Column<string>(type: "TEXT", nullable: true),
                    Conflicts = table.Column<string>(type: "TEXT", nullable: true),
                    Breaks = table.Column<string>(type: "TEXT", nullable: true),
                    Replaces = table.Column<string>(type: "TEXT", nullable: true),
                    Provides = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    MultiArch = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalMaintainer = table.Column<string>(type: "TEXT", nullable: true),
                    Filename = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<string>(type: "TEXT", nullable: false),
                    SHA256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MD5sum = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SHA1 = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SHA512 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApkgDebPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApkgDebPackages_ApkgRevisions_ApkgRevisionId",
                        column: x => x.ApkgRevisionId,
                        principalTable: "ApkgRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApkgDebPackages_AptRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "AptRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApkgDebPackages_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApkgDebPackages_ApkgRevisionId",
                table: "ApkgDebPackages",
                column: "ApkgRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgDebPackages_RepositoryId_Package_Architecture",
                table: "ApkgDebPackages",
                columns: new[] { "RepositoryId", "Package", "Architecture" });

            migrationBuilder.CreateIndex(
                name: "IX_ApkgDebPackages_UploadedByUserId",
                table: "ApkgDebPackages",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgPackages_Name_Distro_Component",
                table: "ApkgPackages",
                columns: new[] { "Name", "Distro", "Component" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApkgPackages_OwnerUserId",
                table: "ApkgPackages",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgRevisions_ApkgPackageId",
                table: "ApkgRevisions",
                column: "ApkgPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ApkgRevisions_UploadedByUserId",
                table: "ApkgRevisions",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AptMirrors_PrimaryBucketId",
                table: "AptMirrors",
                column: "PrimaryBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AptMirrors_SecondaryBucketId",
                table: "AptMirrors",
                column: "SecondaryBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_BucketId",
                table: "AptPackages",
                column: "BucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_Filename",
                table: "AptPackages",
                column: "Filename");

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_Package_Version_Architecture_Component",
                table: "AptPackages",
                columns: new[] { "Package", "Version", "Architecture", "Component" });

            migrationBuilder.CreateIndex(
                name: "IX_AptPackages_SHA256",
                table: "AptPackages",
                column: "SHA256");

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_CertificateId",
                table: "AptRepositories",
                column: "CertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_MirrorId",
                table: "AptRepositories",
                column: "MirrorId");

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_PrimaryBucketId",
                table: "AptRepositories",
                column: "PrimaryBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AptRepositories_SecondaryBucketId",
                table: "AptRepositories",
                column: "SecondaryBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DependencyCheckReports_RepositoryId",
                table: "DependencyCheckReports",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_KeyHash",
                table: "UserApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_UserId",
                table: "UserApiKeys",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApkgDebPackages");

            migrationBuilder.DropTable(
                name: "AptPackages");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "DependencyCheckReports");

            migrationBuilder.DropTable(
                name: "GlobalSettings");

            migrationBuilder.DropTable(
                name: "UserApiKeys");

            migrationBuilder.DropTable(
                name: "ApkgRevisions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AptRepositories");

            migrationBuilder.DropTable(
                name: "ApkgPackages");

            migrationBuilder.DropTable(
                name: "AptCertificates");

            migrationBuilder.DropTable(
                name: "AptMirrors");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "AptBuckets");
        }
    }
}
