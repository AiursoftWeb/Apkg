using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class DebBuilderTests
{
    private readonly DebBuilder _builder;

    public DebBuilderTests()
    {
        _builder = new DebBuilder(
            new ConditionEvaluator(),
            NullLogger<DebBuilder>.Instance);
    }

    // ── BuildControl ──────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildControl_BasicFields()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "2.1.0",
            PackageDescription = "A test package\nWith long description.\nMultiple lines.",
            Maintainer = "Me <me@example.com>"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Package: my-pkg"));
        Assert.IsTrue(control.Contains("Version: 2.1.0"));
        Assert.IsTrue(control.Contains("Architecture: amd64"));
        Assert.IsTrue(control.Contains("Maintainer: Me <me@example.com>"));
        Assert.IsTrue(control.Contains("Installed-Size: __INSTALLED_SIZE__"));
    }

    [TestMethod]
    public void BuildControl_DescriptionFormatting()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "Short description\nLong description line 1.\nLong description line 2."
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Description: Short description"));
        Assert.IsTrue(control.Contains(" Long description line 1."));
        Assert.IsTrue(control.Contains(" Long description line 2."));
    }

    [TestMethod]
    public void BuildControl_EmptyLineInDescription()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "Short\n\nParagraph 2."
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains(" ."));
    }

    [TestMethod]
    public void BuildControl_WithDepends()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Maintainer = "Test <test@example.com>"
        };

        var control = DebBuilder.BuildControl(project, "amd64", ["libc6", "libssl3 (>= 3.0)"]);

        Assert.IsTrue(control.Contains("Depends: libc6, libssl3 (>= 3.0)"));
    }

    [TestMethod]
    public void BuildControl_NoDepends()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsFalse(control.Contains("Depends:"));
    }

    [TestMethod]
    public void BuildControl_WithProvides()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Provides = "virtual-package"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Provides: virtual-package"));
    }

    [TestMethod]
    public void BuildControl_WithConflicts()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Conflicts = "old-package"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Conflicts: old-package"));
    }

    [TestMethod]
    public void BuildControl_WithReplaces()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Replaces = "old-package"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Replaces: old-package"));
    }

    [TestMethod]
    public void BuildControl_WithHomepage()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            PackageHomepage = "https://example.com"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Homepage: https://example.com"));
    }

    [TestMethod]
    public void BuildControl_MaintainerFallsBackToAuthors()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Maintainer = "",
            PackageAuthors = "Alice <alice@example.com>"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Maintainer: Alice <alice@example.com>"));
    }

    [TestMethod]
    public void BuildControl_MaintainerTakesPrecedence()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Maintainer = "Bob <bob@example.com>",
            PackageAuthors = "Alice <alice@example.com>"
        };

        var control = DebBuilder.BuildControl(project, "amd64", []);

        Assert.IsTrue(control.Contains("Maintainer: Bob <bob@example.com>"));
    }

    // ── NormalizeTargetPath ───────────────────────────────────────────────────

    [TestMethod]
    public void NormalizeTargetPath_StripsLeadingSlash()
    {
        var result = DebBuilder.NormalizeTargetPath("/usr/bin/app");
        Assert.IsTrue(result.StartsWith("usr"));
        Assert.IsFalse(result.StartsWith("/"));
    }

    [TestMethod]
    public void NormalizeTargetPath_NoLeadingSlash_Unchanged()
    {
        var result = DebBuilder.NormalizeTargetPath("usr/bin/app");
        Assert.AreEqual("usr/bin/app".Replace('/', Path.DirectorySeparatorChar), result);
    }

    [TestMethod]
    public void NormalizeTargetPath_DotSlashPrefix_KeptIntact()
    {
        var result = DebBuilder.NormalizeTargetPath("./usr/bin/app");
        Assert.AreEqual(("./usr/bin/app".Replace('/', Path.DirectorySeparatorChar)), result);
    }

    // ── Systemd postinst/postrm/prerm generation (commit e03a598) ──────────────

    [TestMethod]
    public async Task BuildAsync_SystemdUnit_CopiesToStaging()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Create the systemd unit source file
            var deployDir = Path.Combine(projectDir, "deploy");
            Directory.CreateDirectory(deployDir);
            await File.WriteAllTextAsync(Path.Combine(deployDir, "myapp.service"),
                "[Unit]\nDescription=My App\n\n[Service]\nExecStart=/usr/bin/myapp\n\n[Install]\nWantedBy=multi-user.target\n");

            var project = new AosprojProject
            {
                PackageName = "myapp",
                PackageVersion = "1.0.0",
                PackageDescription = "Test app",
                Maintainer = "Test <test@example.com>",
                TargetDistro = "ubuntu",
                TargetSuites = "jammy",
                SystemdUnits =
                {
                    new SystemdUnitItem
                    {
                        Source = "deploy/myapp.service",
                        AutoEnable = true
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            // Check staging directory
            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var systemdDest = Path.Combine(staging, "lib", "systemd", "system", "myapp.service");
            Assert.IsTrue(File.Exists(systemdDest), "Systemd unit should be copied to staging.");

            // Check DEBIAN/postinst exists and has systemd enable+start
            var postinst = Path.Combine(staging, "DEBIAN", "postinst");
            Assert.IsTrue(File.Exists(postinst), "postinst should be generated.");
            var postinstContent = await File.ReadAllTextAsync(postinst);
            Assert.IsTrue(postinstContent.Contains("systemctl enable myapp.service"));
            Assert.IsTrue(postinstContent.Contains("systemctl start myapp.service"));

            // Check DEBIAN/prerm exists and has systemd stop
            var prerm = Path.Combine(staging, "DEBIAN", "prerm");
            Assert.IsTrue(File.Exists(prerm), "prerm should be generated.");
            var prermContent = await File.ReadAllTextAsync(prerm);
            Assert.IsTrue(prermContent.Contains("systemctl stop myapp.service"));

            // Check DEBIAN/postrm exists and has systemd disable
            var postrm = Path.Combine(staging, "DEBIAN", "postrm");
            Assert.IsTrue(File.Exists(postrm), "postrm should be generated.");
            var postrmContent = await File.ReadAllTextAsync(postrm);
            Assert.IsTrue(postrmContent.Contains("systemctl disable myapp.service"));

            // Check the .deb was produced
            var debPath = Path.Combine(outputDir, "myapp_1.0.0_jammy_amd64.deb");
            Assert.IsTrue(File.Exists(debPath), ".deb file should be created.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_SystemdUnitAutoEnableFalse()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var deployDir = Path.Combine(projectDir, "deploy");
            Directory.CreateDirectory(deployDir);
            await File.WriteAllTextAsync(Path.Combine(deployDir, "monitor.service"),
                "[Unit]\nDescription=Monitor\n\n[Service]\nExecStart=/usr/bin/monitor\n");

            var project = new AosprojProject
            {
                PackageName = "monitor",
                PackageVersion = "1.0.0",
                PackageDescription = "Monitor service",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                SystemdUnits =
                {
                    new SystemdUnitItem
                    {
                        Source = "deploy/monitor.service",
                        AutoEnable = false
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var systemdDest = Path.Combine(staging, "lib", "systemd", "system", "monitor.service");
            Assert.IsTrue(File.Exists(systemdDest), "Systemd unit should still be copied to staging even when not auto-enabled.");

            // No postinst should be generated (no auto-enable, no custom postinst)
            Assert.IsFalse(File.Exists(Path.Combine(staging, "DEBIAN", "postinst")),
                "postinst should not be created when no auto-enable units and no custom postinst.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_IncludeScript_GetsExecutable()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var scriptsDir = Path.Combine(projectDir, "scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "helper.sh"), "#!/bin/sh\necho hello\n");

            var project = new AosprojProject
            {
                PackageName = "script-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Script package",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeScripts =
                {
                    new IncludeScriptItem
                    {
                        Source = "scripts/helper.sh",
                        Target = "/usr/lib/script-pkg/helper"
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var scriptDest = Path.Combine(staging, "usr", "lib", "script-pkg", "helper");
            Assert.IsTrue(File.Exists(scriptDest), "IncludeScript should be copied.");

            // Verify 0755 permissions — this is the key difference from IncludeFile
            try
            {
                var mode = File.GetUnixFileMode(scriptDest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserExecute), "IncludeScript should be user-executable (0755).");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupExecute), "IncludeScript should be group-executable (0755).");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherExecute), "IncludeScript should be other-executable (0755).");
            }
            catch (PlatformNotSupportedException)
            {
                // Permissions only verifiable on Unix
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PrebuildCommand_Runs()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var markerFile = Path.Combine(tempDir, "prebuild-ran.txt");

            var project = new AosprojProject
            {
                PackageName = "prebuild-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Prebuild test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                PrebuildCommands =
                {
                    new PrebuildCommandItem { Run = $"echo 'ran' > {markerFile}" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            Assert.IsTrue(File.Exists(markerFile), "Prebuild command should have created marker file.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_ConditionalItem_Respected()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Create two files
            File.WriteAllText(Path.Combine(projectDir, "amd64-lib.so"), "amd64");
            File.WriteAllText(Path.Combine(projectDir, "arm64-lib.so"), "arm64");

            var project = new AosprojProject
            {
                PackageName = "cond-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Conditional test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFiles =
                {
                    new IncludeFileItem
                    {
                        Source = "amd64-lib.so",
                        Target = "/usr/lib/lib.so",
                        Condition = "'$(Arch)' == 'amd64'"
                    },
                    new IncludeFileItem
                    {
                        Source = "arm64-lib.so",
                        Target = "/usr/lib/lib.so",
                        Condition = "'$(Arch)' == 'arm64'"
                    }
                }
            };

            // Build for amd64 — should pick amd64-lib.so
            var amd64Output = Path.Combine(outputDir, "amd64");
            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", amd64Output);

            var stagingAmd64 = Path.Combine(projectDir, "obj", "jammy_amd64");
            var destAmd64 = Path.Combine(stagingAmd64, "usr", "lib", "lib.so");
            Assert.IsTrue(File.Exists(destAmd64));
            Assert.AreEqual("amd64", await File.ReadAllTextAsync(destAmd64));

            // Build for arm64 — should pick arm64-lib.so, not amd64-lib.so
            var arm64Output = Path.Combine(outputDir, "arm64");
            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "arm64", arm64Output);

            var stagingArm64 = Path.Combine(projectDir, "obj", "jammy_arm64");
            var destArm64 = Path.Combine(stagingArm64, "usr", "lib", "lib.so");
            Assert.IsTrue(File.Exists(destArm64));
            Assert.AreEqual("arm64", await File.ReadAllTextAsync(destArm64));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_ConfFile_GeneratesConffilesList()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var configDir = Path.Combine(projectDir, "config");
            Directory.CreateDirectory(configDir);
            await File.WriteAllTextAsync(Path.Combine(configDir, "app.conf"), "key=value\n");

            var project = new AosprojProject
            {
                PackageName = "conf-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Conf file test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                ConfFiles =
                {
                    new ConfFileItem
                    {
                        Source = "config/app.conf",
                        Target = "/etc/app/app.conf"
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var conffilesPath = Path.Combine(staging, "DEBIAN", "conffiles");
            Assert.IsTrue(File.Exists(conffilesPath), "conffiles should be generated.");
            var conffiles = await File.ReadAllTextAsync(conffilesPath);
            Assert.IsTrue(conffiles.Contains("/etc/app/app.conf"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_InstalledSize_InControl()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            File.WriteAllText(Path.Combine(projectDir, "readme.txt"), "Some content here for a reasonable file size.");

            var project = new AosprojProject
            {
                PackageName = "size-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Size test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFiles =
                {
                    new IncludeFileItem
                    {
                        Source = "readme.txt",
                        Target = "/usr/share/doc/size-pkg/readme.txt"
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var control = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "control"));
            // Installed-Size should be a positive integer (kibibytes), not the placeholder
            Assert.IsTrue(control.Contains("Installed-Size: "));
            Assert.IsFalse(control.Contains("__INSTALLED_SIZE__"), "Placeholder should be replaced.");
            var match = System.Text.RegularExpressions.Regex.Match(control, @"Installed-Size: (\d+)");
            Assert.IsTrue(match.Success);
            var size = int.Parse(match.Groups[1].Value);
            Assert.IsTrue(size >= 1, "Installed-Size should be at least 1 KB.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PostInstallScript_Appended()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var scriptsDir = Path.Combine(projectDir, "scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "postinst.sh"), "echo \"Custom post-install\"\n");

            var project = new AosprojProject
            {
                PackageName = "postinst-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Post-install test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                PostInstallScripts =
                {
                    new PostInstallScriptItem { Source = "scripts/postinst.sh" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var postinst = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "postinst"));
            Assert.IsTrue(postinst.Contains("Custom post-install"), "Custom postinst script should be included.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PreRemoveScript_Appended()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var scriptsDir = Path.Combine(projectDir, "scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "prerm.sh"), "echo \"Custom pre-remove\"\n");

            var project = new AosprojProject
            {
                PackageName = "prerm-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Pre-remove test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                PreRemoveScripts =
                {
                    new PreRemoveScriptItem { Source = "scripts/prerm.sh" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var prerm = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "prerm"));
            Assert.IsTrue(prerm.Contains("Custom pre-remove"), "Custom prerm script should be included.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "deb-builder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
