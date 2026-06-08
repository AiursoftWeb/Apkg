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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", ["libc6", "libssl3 (>= 3.0)"], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

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
#pragma warning disable CA1416
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
            Assert.AreEqual(5, size, "Installed-Size should count each installed directory and file in 1 KiB units.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task ComputeDirectorySizeKbAsync_RoundsEachFileAndDirectoryIndividually()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var usrShareApp = Path.Combine(tempDir, "usr", "share", "app");
            Directory.CreateDirectory(usrShareApp);
            await File.WriteAllTextAsync(Path.Combine(usrShareApp, "a.txt"), "a");
            await File.WriteAllTextAsync(Path.Combine(usrShareApp, "b.txt"), "b");

            Directory.CreateDirectory(Path.Combine(tempDir, "DEBIAN"));
            await File.WriteAllTextAsync(Path.Combine(tempDir, "DEBIAN", "control"), "ignored");

            var sizeKb = await DebBuilder.ComputeDirectorySizeKbAsync(tempDir);

            Assert.AreEqual(5L, sizeKb,
                "Installed-Size should round each regular file to 1 KiB, count each directory as 1 KiB, and ignore DEBIAN metadata.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task ComputeDirectorySizeKbAsync_CountsSymlinks()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var binDir = Path.Combine(tempDir, "usr", "bin");
            Directory.CreateDirectory(binDir);

            var targetFile = Path.Combine(binDir, "tool");
            await File.WriteAllTextAsync(targetFile, "x");

            var symlinkPath = Path.Combine(binDir, "tool-link");
            File.CreateSymbolicLink(symlinkPath, "tool");

            var sizeKb = await DebBuilder.ComputeDirectorySizeKbAsync(tempDir);

            Assert.AreEqual(4L, sizeKb,
                "Installed-Size should include directory baselines plus a 1 KiB charge for the symlink itself.");
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

    [TestMethod]
    public async Task BuildAsync_PreInstallScript_Appended()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var scriptsDir = Path.Combine(projectDir, "scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "preinst.sh"), "echo \"Custom pre-install\"\n");

            var project = new AosprojProject
            {
                PackageName = "preinst-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Pre-install test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                PreInstallScripts =
                {
                    new PreInstallScriptItem { Source = "scripts/preinst.sh" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var preinst = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "preinst"));
            Assert.IsTrue(preinst.Contains("Custom pre-install"), "Custom preinst script should be included.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PostRemoveScript_Appended()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var scriptsDir = Path.Combine(projectDir, "scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "postrm.sh"), "echo \"Custom post-remove\"\n");

            var project = new AosprojProject
            {
                PackageName = "postrm-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Post-remove test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                PostRemoveScripts =
                {
                    new PostRemoveScriptItem { Source = "scripts/postrm.sh" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var postrm = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "postrm"));
            Assert.IsTrue(postrm.Contains("Custom post-remove"), "Custom postrm script should be included.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Upstream control merging ──────────────────────────────────────────────

    [TestMethod]
    public void BuildControl_UpstreamMergesDepends()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Package"] = "base-files",
            ["Version"] = "13ubuntu10",
            ["Depends"] = "libc6, libssl3",
            ["Section"] = "admin",
            ["Priority"] = "required"
        };

        // Merge local depends with upstream (as BuildAsync does)
        var merged = DebBuilder.MergeDepends(["libc6", "my-new-dep"], upstreamControl);
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", merged, string.Empty, string.Empty, upstreamControl);

        Assert.IsTrue(control.Contains("Depends: libc6, libssl3, my-new-dep"));
        Assert.IsTrue(control.Contains("Section: admin"));
        Assert.IsTrue(control.Contains("Priority: required"));
    }

    [TestMethod]
    public void BuildControl_UpstreamDependsDedup_ByBaseName()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Depends"] = "libc6 (>= 2.34), libssl3"
        };

        // "libc6" should be dedup'd even though upstream has version constraint
        var merged = DebBuilder.MergeDepends(["libc6"], upstreamControl);
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", merged, string.Empty, string.Empty, upstreamControl);

        Assert.IsTrue(control.Contains("Depends: libc6 (>= 2.34), libssl3"));
        Assert.IsFalse(control.Contains("libc6, libc6"));
    }

    [TestMethod]
    public void BuildControl_UpstreamFallsBackToHomepage()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            PackageHomepage = "" // not set locally
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Homepage"] = "https://ubuntu.com"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Homepage: https://ubuntu.com"));
    }

    [TestMethod]
    public void BuildControl_LocalHomepageOverridesUpstream()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            PackageHomepage = "https://anduinos.com"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Homepage"] = "https://ubuntu.com"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Homepage: https://anduinos.com"));
    }

    [TestMethod]
    public void BuildControl_LocalProvidesOverridesUpstream()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Provides = "my-virtual-pkg"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Provides"] = "upstream-virtual-pkg"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Provides: my-virtual-pkg"));
    }

    [TestMethod]
    public void BuildControl_ProvidesFallsBackToUpstream()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Provides = ""
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Provides"] = "upstream-virtual-pkg"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Provides: upstream-virtual-pkg"));
    }

    [TestMethod]
    public void BuildControl_WithBreaks()
    {
        var project = new AosprojProject
        {
            PackageName = "test",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Breaks = "old-pkg (<< 1.5)"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);

        Assert.IsTrue(control.Contains("Breaks: old-pkg (<< 1.5)"), control);
    }

    [TestMethod]
    public void BuildControl_LocalSectionPriorityOverrideUpstream()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Section = "admin",
            Priority = "required"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Section"] = "misc",
            ["Priority"] = "optional"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Section: admin"), control);
        Assert.IsTrue(control.Contains("Priority: required"), control);
    }

    [TestMethod]
    public void BuildControl_SectionPriorityFallBackToUpstream()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Section = "",  // empty → use upstream
            Priority = ""  // empty → use upstream
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Section"] = "admin",
            ["Priority"] = "required"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Section: admin"), control);
        Assert.IsTrue(control.Contains("Priority: required"), control);
    }

    [TestMethod]
    public void BuildControl_BreaksFallsBackToUpstream()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Breaks = ""
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Breaks"] = "upstream-old-pkg"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Breaks: upstream-old-pkg"), control);
    }

    [TestMethod]
    public void BuildControl_LocalBreaksOverridesUpstream()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Breaks = "local-old-pkg"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Breaks"] = "upstream-old-pkg"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Breaks: local-old-pkg"), control);
        Assert.IsFalse(control.Contains("upstream-old-pkg"), control);
    }

    [TestMethod]
    public void BuildControl_UpstreamConflictsReplaces_FallBack()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc",
            Conflicts = "",
            Replaces = ""
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Conflicts"] = "old-pkg",
            ["Replaces"] = "old-pkg"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Conflicts: old-pkg"));
        Assert.IsTrue(control.Contains("Replaces: old-pkg"));
    }

    [TestMethod]
    public void BuildControl_NoUpstream_WritesDebianStandardSectionPriority()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty);
        // Section and Priority always output — three-tier fallback
        // (local → upstream → Debian standard) ensures values are never missing.
        Assert.IsTrue(control.Contains("Section: utils"), control);
        Assert.IsTrue(control.Contains("Priority: optional"), control);
        Assert.IsFalse(control.Contains("Breaks:"), control);
    }

    [TestMethod]
    public void BuildControl_WithRecommends_WritesField()
    {
        var project = new AosprojProject { PackageName = "meta-pkg", PackageVersion = "1.0", PackageDescription = "meta" };
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "all", [],
            "pkg-a, pkg-b", string.Empty);
        Assert.IsTrue(control.Contains("Recommends: pkg-a, pkg-b"), control);
        Assert.IsFalse(control.Contains("Suggests:"), control);
    }

    [TestMethod]
    public void BuildControl_WithSuggests_WritesField()
    {
        var project = new AosprojProject { PackageName = "meta-pkg", PackageVersion = "1.0", PackageDescription = "meta" };
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "all", [],
            string.Empty, "optional-tool");
        Assert.IsFalse(control.Contains("Recommends:"), control);
        Assert.IsTrue(control.Contains("Suggests: optional-tool"), control);
    }

    [TestMethod]
    public void BuildControl_RecommendsFallsBackToUpstream()
    {
        var project = new AosprojProject { PackageName = "my-pkg", PackageVersion = "1.0", PackageDescription = "d" };
        var upstream = new Dictionary<string, string> { ["Recommends"] = "upstream-rec", ["Suggests"] = "upstream-sug" };
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [],
            string.Empty, string.Empty, upstream);
        Assert.IsTrue(control.Contains("Recommends: upstream-rec"), control);
        Assert.IsTrue(control.Contains("Suggests: upstream-sug"), control);
    }

    [TestMethod]
    public void BuildControl_LocalRecommendsOverridesUpstream()
    {
        var project = new AosprojProject { PackageName = "my-pkg", PackageVersion = "1.0", PackageDescription = "d" };
        var upstream = new Dictionary<string, string> { ["Recommends"] = "upstream-rec" };
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [],
            "local-rec", string.Empty, upstream);
        Assert.IsTrue(control.Contains("Recommends: local-rec"), control);
        Assert.IsFalse(control.Contains("upstream-rec"), control);
    }

    [TestMethod]
    public void AosprojSerializer_RecommendAndSuggest_RoundTrip()
    {
        var project = new AosprojProject
        {
            PackageName = "meta", PackageVersion = "1.0", PackageDescription = "d",
            TargetSuites = "noble", TargetArchitectures = "all"
        };
        project.Recommends.Add(new ConditionalValue { Value = "pkg-a" });
        project.Recommends.Add(new ConditionalValue { Value = "pkg-b" });
        project.Suggests.Add(new ConditionalValue { Value = "optional-tool" });

        var serializer = new AosprojSerializer();
        var doc = serializer.Serialize(project);
        var loaded = serializer.Deserialize(doc);

        Assert.AreEqual(2, loaded.Recommends.Count);
        Assert.AreEqual("pkg-a", loaded.Recommends[0].Value);
        Assert.AreEqual("pkg-b", loaded.Recommends[1].Value);
        Assert.AreEqual(1, loaded.Suggests.Count);
        Assert.AreEqual("optional-tool", loaded.Suggests[0].Value);
    }

    [TestMethod]
    public void BuildControl_UpstreamVersionVariable_ResolvedCorrectly()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "$(UpstreamVersion)-anduinos",
            PackageDescription = "desc"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Version"] = "13ubuntu10"
        };

        var resolvedVersion = "13ubuntu10-anduinos";
        var control = DebBuilder.BuildControl(project, resolvedVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);

        Assert.IsTrue(control.Contains("Version: 13ubuntu10-anduinos"));
        Assert.IsFalse(control.Contains("$(UpstreamVersion)"));
    }

    // ── ParseControlFile ──────────────────────────────────────────────────────

    [TestMethod]
    public void ParseControlFile_BasicFields()
    {
        // ParseControlFile is private; tested indirectly through BuildControl with upstreamControl
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Package"] = "base-files",
            ["Version"] = "13ubuntu10",
            ["Architecture"] = "amd64",
            ["Maintainer"] = "Ubuntu Developers <ubuntu-devel-discuss@lists.ubuntu.com>",
            ["Installed-Size"] = "328"
        };

        var result = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], string.Empty, string.Empty, upstreamControl);
        // Verify upstream fields don't leak through (local always wins for identity fields)
        Assert.IsTrue(result.Contains("Package: my-pkg"));
        Assert.IsTrue(result.Contains("Version: 1.0"));
    }

    [TestMethod]
    public void BuildControl_UpstreamDependsWithVersionConstraints_Merged()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc"
        };

        var upstreamControl = new Dictionary<string, string>
        {
            ["Depends"] = "base-files (>= 11), libc6 (>= 2.34), libssl3 (>= 3.0.2)"
        };

        var merged = DebBuilder.MergeDepends(["my-dep (>= 2.0)"], upstreamControl);
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", merged, string.Empty, string.Empty, upstreamControl);
        Assert.IsTrue(control.Contains("Depends: base-files (>= 11), libc6 (>= 2.34), libssl3 (>= 3.0.2), my-dep (>= 2.0)"));
    }

    // ── MergeDepends ─────────────────────────────────────────────────────────

    [TestMethod]
    public void MergeDepends_NoUpstream_ReturnsLocal()
    {
        var local = new List<string> { "libc6", "libssl3" };
        var result = DebBuilder.MergeDepends(local, null);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("libc6", result[0]);
        Assert.AreEqual("libssl3", result[1]);
    }

    [TestMethod]
    public void MergeDepends_EmptyUpstreamDepends_ReturnsLocal()
    {
        var local = new List<string> { "libc6" };
        var upstream = new Dictionary<string, string> { ["Depends"] = "" };
        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("libc6", result[0]);
    }

    [TestMethod]
    public void MergeDepends_UpstreamWithoutDependsKey_ReturnsLocal()
    {
        var local = new List<string> { "libc6" };
        var upstream = new Dictionary<string, string> { ["Package"] = "test" };
        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void MergeDepends_DedupByBaseName()
    {
        var local = new List<string> { "libc6", "my-dep" };
        var upstream = new Dictionary<string, string> { ["Depends"] = "libc6 (>= 2.34), libssl3" };
        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("libc6 (>= 2.34)", result[0]); // upstream version wins
        Assert.AreEqual("libssl3", result[1]);
        Assert.AreEqual("my-dep", result[2]); // local-only appended
    }

    [TestMethod]
    public void MergeDepends_NoOverlap_Concatenates()
    {
        var local = new List<string> { "my-dep" };
        var upstream = new Dictionary<string, string> { ["Depends"] = "libc6, libssl3" };
        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("libc6", result[0]);
        Assert.AreEqual("libssl3", result[1]);
        Assert.AreEqual("my-dep", result[2]);
    }

    [TestMethod]
    public void MergeDepends_EmptyLocal_ReturnsUpstream()
    {
        var local = new List<string>();
        var upstream = new Dictionary<string, string> { ["Depends"] = "libc6, libssl3" };
        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("libc6", result[0]);
        Assert.AreEqual("libssl3", result[1]);
    }

    // ── SuppressUpstreamDependencies ──────────────────────────────────────────

    [TestMethod]
    public void MergeDepends_SuppressRemovesSinglePackage()
    {
        // Simulates what BuildAsync does: strip ubuntu-pro-client from upstream
        // Depends before calling MergeDepends.
        var local = new List<string> { "anduinos-software-properties-common" };
        var upstream = new Dictionary<string, string>
        {
            ["Depends"] = "python3, ubuntu-pro-client, ubuntu-drivers-common"
        };
        var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ubuntu-pro-client"
        };

        // Replicate the filtering logic from BuildAsync
        if (upstream.TryGetValue("Depends", out var upsDeps) && !string.IsNullOrWhiteSpace(upsDeps))
        {
            var filtered = upsDeps
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim())
                .Where(d => !suppress.Contains(d.Split(' ', 2)[0]))
                .ToList();
            upstream["Depends"] = string.Join(", ", filtered);
        }

        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("python3", result[0]);
        Assert.AreEqual("ubuntu-drivers-common", result[1]);
        Assert.AreEqual("anduinos-software-properties-common", result[2]);
    }

    [TestMethod]
    public void MergeDepends_SuppressRemovesMultiplePackages()
    {
        var local = new List<string> { "my-dep" };
        var upstream = new Dictionary<string, string>
        {
            ["Depends"] = "python3, ubuntu-pro-client, ubuntu-advantage-desktop-daemon, ubuntu-drivers-common"
        };
        var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ubuntu-pro-client", "ubuntu-advantage-desktop-daemon"
        };

        if (upstream.TryGetValue("Depends", out var upsDeps) && !string.IsNullOrWhiteSpace(upsDeps))
        {
            var filtered = upsDeps
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim())
                .Where(d => !suppress.Contains(d.Split(' ', 2)[0]))
                .ToList();
            upstream["Depends"] = string.Join(", ", filtered);
        }

        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("python3", result[0]);
        Assert.AreEqual("ubuntu-drivers-common", result[1]);
        Assert.AreEqual("my-dep", result[2]);
    }

    [TestMethod]
    public void MergeDepends_SuppressWithVersionConstraint()
    {
        var local = new List<string>();
        var upstream = new Dictionary<string, string>
        {
            ["Depends"] = "python3, ubuntu-pro-client (>= 33), ubuntu-drivers-common (>= 1:0.9.6)"
        };
        var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ubuntu-pro-client"
        };

        if (upstream.TryGetValue("Depends", out var upsDeps) && !string.IsNullOrWhiteSpace(upsDeps))
        {
            var filtered = upsDeps
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim())
                .Where(d => !suppress.Contains(d.Split(' ', 2)[0]))
                .ToList();
            upstream["Depends"] = string.Join(", ", filtered);
        }

        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("python3", result[0]);
        Assert.AreEqual("ubuntu-drivers-common (>= 1:0.9.6)", result[1]);
    }

    [TestMethod]
    public void MergeDepends_SuppressNonExistent_NoChange()
    {
        var local = new List<string>();
        var upstream = new Dictionary<string, string>
        {
            ["Depends"] = "python3, libc6"
        };
        var suppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nonexistent-package"
        };

        if (upstream.TryGetValue("Depends", out var upsDeps) && !string.IsNullOrWhiteSpace(upsDeps))
        {
            var filtered = upsDeps
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim())
                .Where(d => !suppress.Contains(d.Split(' ', 2)[0]))
                .ToList();
            upstream["Depends"] = string.Join(", ", filtered);
        }

        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("python3", result[0]);
        Assert.AreEqual("libc6", result[1]);
    }

    [TestMethod]
    public void MergeDepends_EmptySuppress_NoChange()
    {
        var local = new List<string>();
        var upstream = new Dictionary<string, string>
        {
            ["Depends"] = "python3, ubuntu-pro-client"
        };
        // Empty suppress set — nothing filtered
        if (upstream.TryGetValue("Depends", out var upsDeps) && !string.IsNullOrWhiteSpace(upsDeps))
        {
            var filtered = upsDeps
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim())
                .ToList();
            upstream["Depends"] = string.Join(", ", filtered);
        }

        var result = DebBuilder.MergeDepends(local, upstream);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("python3", result[0]);
        Assert.AreEqual("ubuntu-pro-client", result[1]);
    }

    // ── Error paths ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildAsync_PrebuildCommandFailure_Throws()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var project = new AosprojProject
            {
                PackageName = "fail-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Prebuild failure test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                PrebuildCommands =
                {
                    new PrebuildCommandItem { Run = "exit 42" }
                }
            };

            try
            {
                await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException ex)
            {
                Assert.IsTrue(ex.Message.Contains("exit 42"));
                Assert.IsTrue(ex.Message.Contains("exit code 42"));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Mixed item types ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildAsync_MixedItemTypes_AllCopied()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Create all source files
            File.WriteAllText(Path.Combine(projectDir, "readme.txt"), "readme");
            Directory.CreateDirectory(Path.Combine(projectDir, "scripts"));
            await File.WriteAllTextAsync(Path.Combine(projectDir, "scripts", "helper.sh"), "#!/bin/sh\necho hi\n");
            Directory.CreateDirectory(Path.Combine(projectDir, "config"));
            await File.WriteAllTextAsync(Path.Combine(projectDir, "config", "app.conf"), "key=val\n");
            Directory.CreateDirectory(Path.Combine(projectDir, "deploy"));
            await File.WriteAllTextAsync(Path.Combine(projectDir, "deploy", "app.service"),
                "[Unit]\nDescription=App\n\n[Service]\nExecStart=/usr/bin/app\n\n[Install]\nWantedBy=multi-user.target\n");
            await File.WriteAllTextAsync(Path.Combine(projectDir, "postinst.sh"), "echo postinstall\n");
            await File.WriteAllTextAsync(Path.Combine(projectDir, "prerm.sh"), "echo preremove\n");
            await File.WriteAllTextAsync(Path.Combine(projectDir, "preinst.sh"), "echo preinstall\n");
            await File.WriteAllTextAsync(Path.Combine(projectDir, "postrm.sh"), "echo postremove\n");

            var project = new AosprojProject
            {
                PackageName = "mixed-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Mixed item types",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFiles =
                {
                    new IncludeFileItem { Source = "readme.txt", Target = "/usr/share/doc/mixed-pkg/readme.txt" }
                },
                IncludeScripts =
                {
                    new IncludeScriptItem { Source = "scripts/helper.sh", Target = "/usr/lib/mixed-pkg/helper" }
                },
                ConfFiles =
                {
                    new ConfFileItem { Source = "config/app.conf", Target = "/etc/mixed-pkg/app.conf" }
                },
                SystemdUnits =
                {
                    new SystemdUnitItem { Source = "deploy/app.service", AutoEnable = true }
                },
                PostInstallScripts =
                {
                    new PostInstallScriptItem { Source = "postinst.sh" }
                },
                PreRemoveScripts =
                {
                    new PreRemoveScriptItem { Source = "prerm.sh" }
                },
                PreInstallScripts =
                {
                    new PreInstallScriptItem { Source = "preinst.sh" }
                },
                PostRemoveScripts =
                {
                    new PostRemoveScriptItem { Source = "postrm.sh" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");

            // IncludeFile
            Assert.IsTrue(File.Exists(Path.Combine(staging, "usr", "share", "doc", "mixed-pkg", "readme.txt")));
            // IncludeScript (executable)
            Assert.IsTrue(File.Exists(Path.Combine(staging, "usr", "lib", "mixed-pkg", "helper")));
            // ConfFile
            Assert.IsTrue(File.Exists(Path.Combine(staging, "etc", "mixed-pkg", "app.conf")));
            // SystemdUnit
            Assert.IsTrue(File.Exists(Path.Combine(staging, "lib", "systemd", "system", "app.service")));
            // PostInstallScript merged into postinst
            var postinst = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "postinst"));
            Assert.IsTrue(postinst.Contains("echo postinstall"));
            // PreRemoveScript merged into prerm
            var prerm = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "prerm"));
            Assert.IsTrue(prerm.Contains("echo preremove"));
            // PreInstallScript merged into preinst
            var preinst = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "preinst"));
            Assert.IsTrue(preinst.Contains("echo preinstall"));
            // PostRemoveScript merged into postrm
            var postrm = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "postrm"));
            Assert.IsTrue(postrm.Contains("echo postremove"));
            // conffiles
            Assert.IsTrue(File.Exists(Path.Combine(staging, "DEBIAN", "conffiles")));
            // .deb produced
            Assert.IsTrue(File.Exists(Path.Combine(outputDir, "mixed-pkg_1.0.0_jammy_amd64.deb")));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── BuildDownloadSpec ────────────────────────────────────────────────────

    [TestMethod]
    public void BuildDownloadSpec_ArchAll_ReturnsPackageSlashSuite()
    {
        var result = DebBuilder.BuildDownloadSpec("base-files", "all", "jammy");
        Assert.AreEqual("base-files/jammy", result);
    }

    [TestMethod]
    public void BuildDownloadSpec_ArchSpecific_ReturnsPackageColonArchSlashSuite()
    {
        var result = DebBuilder.BuildDownloadSpec("libc6", "amd64", "noble");
        Assert.AreEqual("libc6:amd64/noble", result);
    }

    [TestMethod]
    public void BuildDownloadSpec_EmptyArch_TreatedAsAll()
    {
        var result = DebBuilder.BuildDownloadSpec("base-files", "", "jammy");
        Assert.AreEqual("base-files/jammy", result);
    }

    // ── StripShebang ─────────────────────────────────────────────────────────

    [TestMethod]
    public void StripShebang_RemovesShebangLine()
    {
        var input = "#!/bin/sh\nset -e\necho hello\n";
        var result = DebBuilder.StripShebang(input);
        Assert.IsFalse(result.StartsWith("#!"));
        Assert.IsTrue(result.Contains("set -e"));
        Assert.IsTrue(result.Contains("echo hello"));
    }

    [TestMethod]
    public void StripShebang_OnlyShebang_ReturnsEmpty()
    {
        var result = DebBuilder.StripShebang("#!/bin/bash\n");
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void StripShebang_NoShebang_Unchanged()
    {
        var input = "set -e\necho hello\n";
        var result = DebBuilder.StripShebang(input);
        Assert.AreEqual("set -e\necho hello\n", result);
    }

    [TestMethod]
    public void StripShebang_TrimsLeadingBlankLines()
    {
        var input = "\n\nset -e\necho hello\n";
        var result = DebBuilder.StripShebang(input);
        Assert.AreEqual("set -e\necho hello\n", result);
    }

    // ── ParseControlFile ─────────────────────────────────────────────────────

    [TestMethod]
    public void ParseControlFile_SimpleFields()
    {
        var input = "Package: base-files\nVersion: 13ubuntu10\nArchitecture: amd64\n";
        var result = DebBuilder.ParseControlFile(input);
        Assert.AreEqual("base-files", result["Package"]);
        Assert.AreEqual("13ubuntu10", result["Version"]);
        Assert.AreEqual("amd64", result["Architecture"]);
    }

    [TestMethod]
    public void ParseControlFile_MultiLineField()
    {
        var input = """
            Package: base-files
            Description: This is a short description
             This is a continuation line.
             Another continuation line.
            Homepage: https://example.com
            """;
        var result = DebBuilder.ParseControlFile(input);
        Assert.AreEqual("base-files", result["Package"]);
        Assert.IsTrue(result["Description"].Contains("This is a short description"));
        Assert.IsTrue(result["Description"].Contains("This is a continuation line."));
        Assert.IsTrue(result["Description"].Contains("Another continuation line."));
        Assert.AreEqual("https://example.com", result["Homepage"]);
    }

    [TestMethod]
    public void ParseControlFile_DependsWithVersionConstraints()
    {
        var input = "Depends: libc6 (>= 2.34), libssl3 (>= 3.0.2)\n";
        var result = DebBuilder.ParseControlFile(input);
        Assert.AreEqual("libc6 (>= 2.34), libssl3 (>= 3.0.2)", result["Depends"]);
    }

    [TestMethod]
    public void ParseControlFile_TabContinuation()
    {
        var input = "Description: Short\n\tTab continuation.\nHomepage: https://x.com\n";
        var result = DebBuilder.ParseControlFile(input);
        Assert.IsTrue(result["Description"].Contains("Tab continuation."));
    }

    [TestMethod]
    public void ParseControlFile_EmptyInput_ReturnsEmpty()
    {
        var result = DebBuilder.ParseControlFile("");
        Assert.AreEqual(0, result.Count);
    }

    // ── ResolveVariables ─────────────────────────────────────────────────────

    [TestMethod]
    public void ResolveVariables_ReplacesSuite()
    {
        var result = DebBuilder.ResolveVariables("$(Suite)", new(), "", "jammy", "");
        Assert.AreEqual("jammy", result);
    }

    [TestMethod]
    public void ResolveVariables_ReplacesDistro()
    {
        var result = DebBuilder.ResolveVariables("$(Distro)", new(), "ubuntu", "", "");
        Assert.AreEqual("ubuntu", result);
    }

    [TestMethod]
    public void ResolveVariables_ReplacesArch()
    {
        var result = DebBuilder.ResolveVariables("$(Arch)", new(), "", "", "amd64");
        Assert.AreEqual("amd64", result);
    }

    [TestMethod]
    public void ResolveVariables_ArchitectureAlias()
    {
        var result = DebBuilder.ResolveVariables("$(Architecture)", new(), "", "", "arm64");
        Assert.AreEqual("arm64", result);
    }

    [TestMethod]
    public void ResolveVariables_ReplacesComponent()
    {
        var project = new AosprojProject { Component = "addon" };
        var result = DebBuilder.ResolveVariables("$(Component)", project, "", "", "");
        Assert.AreEqual("addon", result);
    }

    [TestMethod]
    public void ResolveVariables_MultipleVariables()
    {
        var project = new AosprojProject { Component = "addon" };
        var result = DebBuilder.ResolveVariables(
            "$(Distro)/dists/$(Suite)/$(Component)/binary-$(Arch)",
            project, "anduinos", "questing", "amd64");
        Assert.AreEqual("anduinos/dists/questing/addon/binary-amd64", result);
    }

    [TestMethod]
    public void ResolveVariables_NoVariables_Unchanged()
    {
        var result = DebBuilder.ResolveVariables("plain text", new(), "", "", "");
        Assert.AreEqual("plain text", result);
    }

    [TestMethod]
    public void ResolveVariables_EmptyInput_ReturnsEmpty()
    {
        var result = DebBuilder.ResolveVariables("", new(), "", "", "");
        Assert.AreEqual("", result);
    }

    [TestMethod]
    public void ResolveVariables_UnknownVariable_Preserved()
    {
        var result = DebBuilder.ResolveVariables("$(Unknown)", new(), "", "", "");
        Assert.AreEqual("$(Unknown)", result);
    }

    // ── ResolvePackageVersion ─────────────────────────────────────────────────

    [TestMethod]
    public void ResolvePackageVersion_ReplacesSuite()
    {
        var result = DebBuilder.ResolvePackageVersion("1.0.0+$(Suite)", "jammy");
        Assert.AreEqual("1.0.0+jammy", result);
    }

    [TestMethod]
    public void ResolvePackageVersion_ReplacesSuiteShortName()
    {
        var map = new Dictionary<string, string> { ["questing-addon"] = "questing" };
        var result = DebBuilder.ResolvePackageVersion("1.0+$(SuiteShortName)", "questing-addon", map);
        Assert.AreEqual("1.0+questing", result);
    }

    [TestMethod]
    public void ResolvePackageVersion_SuiteShortNameFallsBackToSuite()
    {
        var map = new Dictionary<string, string>();
        var result = DebBuilder.ResolvePackageVersion("1.0+$(SuiteShortName)", "jammy", map);
        Assert.AreEqual("1.0+jammy", result);
    }

    [TestMethod]
    public void ResolvePackageVersion_NullMap_FallsBackToSuite()
    {
        var result = DebBuilder.ResolvePackageVersion("1.0+$(SuiteShortName)", "noble");
        Assert.AreEqual("1.0+noble", result);
    }

    [TestMethod]
    public void ResolvePackageVersion_NoVariables_Unchanged()
    {
        var result = DebBuilder.ResolvePackageVersion("2.1.0", "jammy");
        Assert.AreEqual("2.1.0", result);
    }

    [TestMethod]
    public void ResolvePackageVersion_MultipleVariables()
    {
        var map = new Dictionary<string, string> { ["noble-addon"] = "noble" };
        var result = DebBuilder.ResolvePackageVersion("$(Suite).$(SuiteShortName).1", "noble-addon", map);
        Assert.AreEqual("noble-addon.noble.1", result);
    }

    [TestMethod]
    public void ResolvePackageVersion_EmptyInput_ReturnsEmpty()
    {
        var result = DebBuilder.ResolvePackageVersion("", "jammy");
        Assert.AreEqual("", result);
    }

    /// <summary>
    /// Regression test for non-deterministic .deb builds.
    ///
    /// Without SOURCE_DATE_EPOCH=0, dpkg-deb embeds the current wall-clock time in the
    /// ar/tar archive headers.  This means two successive builds of identical source
    /// produce .deb files that differ byte-for-byte (and therefore have different SHA256
    /// hashes).  In a multi-suite setup (noble-addon, questing-addon, resolute-addon) each
    /// CI target rebuilds the same package → three different SHA256 values end up in the
    /// AptPackages table for the same Filename.  Because GetLocalPoolPath uses
    /// FirstOrDefaultAsync without ORDER BY, it can return any of those rows, while apt's
    /// Packages index was generated from a different one → "File has unexpected size".
    ///
    /// FIX: RunCommandAsync now sets Environment["SOURCE_DATE_EPOCH"] = "0" so that
    /// dpkg-deb always produces byte-for-byte identical output for the same source.
    /// </summary>
    [TestMethod]
    public async Task BuildAsync_RepeatBuildsProduceIdenticalDeb()
    {
        var tempDir1 = CreateTestDirectory();
        var tempDir2 = CreateTestDirectory();
        try
        {
            // Build the same package twice from identical source trees
            // but with DIFFERENT file mtimes — simulating two separate CI downloads.
            // Without SOURCE_DATE_EPOCH=0, dpkg-deb embeds file timestamps in the
            // ar/tar headers, so the resulting .deb differs byte-for-byte.
            var baseMtime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var (tempDir, mtime) in new[] {
                (tempDir1, baseMtime),
                (tempDir2, baseMtime.AddSeconds(60))  // simulate a later download
            })
            {
                var projectDir = Path.Combine(tempDir, "project");
                var outputDir = Path.Combine(tempDir, "output");
                Directory.CreateDirectory(projectDir);
                var srcFile = Path.Combine(projectDir, "hello.txt");
                await File.WriteAllTextAsync(srcFile, "hello world\n");
                File.SetLastWriteTimeUtc(srcFile, mtime); // different mtime per build!

                var project = new AosprojProject
                {
                    PackageName = "repro-pkg",
                    PackageVersion = "1.0.0",
                    PackageDescription = "Reproducible build test",
                    Maintainer = "Test <test@example.com>",
                    TargetSuites = "jammy",
                    IncludeFiles =
                    {
                        new IncludeFileItem { Source = "hello.txt", Target = "/usr/share/repro-pkg/hello.txt" }
                    }
                };

                await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);
            }

            var deb1 = Path.Combine(tempDir1, "output", "repro-pkg_1.0.0_jammy_amd64.deb");
            var deb2 = Path.Combine(tempDir2, "output", "repro-pkg_1.0.0_jammy_amd64.deb");

            Assert.IsTrue(File.Exists(deb1), "First build output must exist.");
            Assert.IsTrue(File.Exists(deb2), "Second build output must exist.");

            var hash1 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(deb1))).ToLowerInvariant();
            var hash2 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(deb2))).ToLowerInvariant();

            Assert.AreEqual(hash1, hash2,
                "Both builds must produce the exact same .deb (SOURCE_DATE_EPOCH=0 must be set). " +
                $"Build 1: {hash1}, Build 2: {hash2}");
        }
        finally
        {
            Directory.Delete(tempDir1, recursive: true);
            Directory.Delete(tempDir2, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PackageVersion_SubstitutesSuiteVariable()
    {
        // Arrange: PackageVersion contains $(Suite) — should expand to the actual suite name.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir  = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(outputDir);

            var project = new AosprojProject
            {
                PackageName        = "my-ext",
                PackageVersion     = "1.0.0+$(Suite)1",   // $(Suite) should be substituted
                PackageDescription = "Suite-specific extension",
                Maintainer         = "Test <test@example.com>",
                TargetDistro       = "ubuntu",
                TargetSuites       = "questing-addon",
            };

            // Act
            await _builder.BuildAsync(projectDir, project, "ubuntu", "questing-addon", "all", outputDir);

            // Assert: $(Suite) expanded to "questing-addon" in the produced .deb filename and control
            var expectedDeb = Path.Combine(outputDir, "my-ext_1.0.0+questing-addon1_questing-addon_all.deb");
            Assert.IsTrue(File.Exists(expectedDeb),
                $"Expected .deb with suite-substituted version to exist at: {expectedDeb}");

            var staging     = Path.Combine(projectDir, "obj", "questing-addon_all");
            var controlText = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "control"));
            Assert.IsTrue(controlText.Contains("Version: 1.0.0+questing-addon1"),
                $"DEBIAN/control should contain substituted version. Actual control:\n{controlText}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PackageVersion_SubstitutesSuiteShortNameVariable()
    {
        // Arrange: PackageVersion contains $(SuiteShortName) — should expand to the short name
        // from SuiteShortNameMap (e.g. "questing-addon" → "questing").
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir  = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(outputDir);

            var project = new AosprojProject
            {
                PackageName             = "my-ext",
                PackageVersion          = "1.0.0+$(SuiteShortName)1",  // $(SuiteShortName) → "questing"
                PackageDescription      = "Suite-specific extension",
                Maintainer              = "Test <test@example.com>",
                TargetDistro            = "ubuntu",
                TargetSuites            = "questing-addon",
                SuiteShortNameMap = "noble-addon=noble questing-addon=questing resolute-addon=resolute",
            };

            // Act
            await _builder.BuildAsync(projectDir, project, "ubuntu", "questing-addon", "all", outputDir);

            // Assert: $(SuiteShortName) expanded to "questing" (not "questing-addon")
            var expectedDeb = Path.Combine(outputDir, "my-ext_1.0.0+questing1_questing-addon_all.deb");
            Assert.IsTrue(File.Exists(expectedDeb),
                $"Expected .deb with short-name-substituted version to exist at: {expectedDeb}");

            var staging     = Path.Combine(projectDir, "obj", "questing-addon_all");
            var controlText = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "control"));
            Assert.IsTrue(controlText.Contains("Version: 1.0.0+questing1"),
                $"DEBIAN/control should contain short-name version. Actual control:\n{controlText}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PackageVersion_SuiteShortNameFallsBackToSuiteWhenNotInMap()
    {
        // Arrange: $(SuiteShortName) with no SuiteShortNameMap entry → falls back to suite name.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir  = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(outputDir);

            var project = new AosprojProject
            {
                PackageName             = "my-ext",
                PackageVersion          = "2.0+$(SuiteShortName)1",
                PackageDescription      = "Suite-specific extension",
                Maintainer              = "Test <test@example.com>",
                TargetDistro            = "ubuntu",
                TargetSuites            = "jammy",
                SuiteShortNameMap = "",  // no map → fall back to suite name
            };

            // Act
            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "all", outputDir);

            // Assert: $(SuiteShortName) fell back to "jammy"
            var expectedDeb = Path.Combine(outputDir, "my-ext_2.0+jammy1_jammy_all.deb");
            Assert.IsTrue(File.Exists(expectedDeb),
                $"Expected .deb with fallback suite name in version: {expectedDeb}");

            var staging     = Path.Combine(projectDir, "obj", "jammy_all");
            var controlText = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "control"));
            Assert.IsTrue(controlText.Contains("Version: 2.0+jammy1"),
                $"DEBIAN/control should contain fallback version. Actual control:\n{controlText}");
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

    // ── SuppressUpstreamScripts ──────────────────────────────────────────────

    [TestMethod]
    public async Task BuildAsync_SuppressUpstreamScripts_ExcludesUpstreamPostinst()
    {
        // Verifies that SuppressUpstreamScripts=true prevents the upstream
        // postinst (which ends with exit 0) from being prepended, so the
        // user's PostInstallScript runs as the sole postinst content.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // ── 1. Build a minimal upstream .deb with a postinst that has exit 0 ──
            var upstreamBuildDir = Path.Combine(tempDir, "upstream-build");
            var upstreamDebianDir = Path.Combine(upstreamBuildDir, "DEBIAN");
            Directory.CreateDirectory(upstreamDebianDir);

            await File.WriteAllTextAsync(Path.Combine(upstreamDebianDir, "control"),
                "Package: fake-upstream\n"
                + "Version: 1.0\n"
                + "Architecture: all\n"
                + "Maintainer: Test <test@example.com>\n"
                + "Description: Fake upstream for SuppressUpstreamScripts test\n");

            var upstreamPostinstPath = Path.Combine(upstreamDebianDir, "postinst");
            await File.WriteAllTextAsync(upstreamPostinstPath,
                "#!/bin/sh\nset -e\n"
                + "case \"$1\" in\n"
                + "    configure)\n"
                + "        echo \"upstream postinst ran\"\n"
                + "    ;;\n"
                + "esac\n"
                + "exit 0\n");
            File.SetUnixFileMode(upstreamPostinstPath,
                UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var upstreamPreinstPath = Path.Combine(upstreamDebianDir, "preinst");
            await File.WriteAllTextAsync(upstreamPreinstPath,
                "#!/bin/sh\nset -e\necho \"upstream preinst ran\"\nexit 0\n");
            File.SetUnixFileMode(upstreamPreinstPath,
                UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var upstreamDebName = "fake-upstream_1.0_all.deb";
            var upstreamDebPath = Path.Combine(tempDir, upstreamDebName);
            await RunAsync("dpkg-deb", ["--build", "--root-owner-group", upstreamBuildDir, upstreamDebPath]);

            // ── 2. Set up a local APT repo with proper dists/ structure ──
            var repoDir = Path.Combine(tempDir, "repo");
            var distsDir = Path.Combine(repoDir, "dists", "jammy", "main", "binary-all");
            Directory.CreateDirectory(distsDir);

            // Put .deb at repo root (relative filename in Packages references it from repo root)
            File.Copy(upstreamDebPath, Path.Combine(repoDir, upstreamDebName), overwrite: true);
            var fi = new FileInfo(Path.Combine(repoDir, upstreamDebName));
            var debSize = fi.Length;

            // Compute hashes
            string md5, sha256;
            using (var fs = fi.OpenRead())
            {
                using var md5Alg = System.Security.Cryptography.MD5.Create();
                md5 = Convert.ToHexStringLower(md5Alg.ComputeHash(fs));
                fs.Position = 0;
                using var sha256Alg = System.Security.Cryptography.SHA256.Create();
                sha256 = Convert.ToHexStringLower(sha256Alg.ComputeHash(fs));
            }

            // Manually craft a Packages file (more reliable than dpkg-scanpackages for file:// repos)
            var packagesContent =
                $"Package: fake-upstream\n"
                + $"Version: 1.0\n"
                + $"Architecture: all\n"
                + $"Maintainer: Test <test@example.com>\n"
                + $"Description: Fake upstream for SuppressUpstreamScripts test\n"
                + $"Filename: {upstreamDebName}\n"
                + $"Size: {debSize}\n"
                + $"MD5sum: {md5}\n"
                + $"SHA256: {sha256}\n";
            var packagesPath = Path.Combine(distsDir, "Packages");
            await File.WriteAllTextAsync(packagesPath, packagesContent);
            await RunAsync("gzip", ["-kf", packagesPath]);

            // apt also tries to fetch Packages for native/foreign architectures;
            // create empty index files to avoid "File not found" errors (exit 100)
            foreach (var extraArch in new[] { "amd64", "i386" })
            {
                var extraDir = Path.Combine(repoDir, "dists", "jammy", "main", $"binary-{extraArch}");
                Directory.CreateDirectory(extraDir);
                var p = Path.Combine(extraDir, "Packages");
                await File.WriteAllTextAsync(p, "");
                await RunAsync("gzip", ["-kf", p]);
            }

            // apt-get download with /suite suffix requires a Release file
            // identifying the suite. apt-ftparchive generates the checksums,
            // then we prepend Suite/Codename headers so apt can find the release.
            var release = await RunAndCaptureAsync("apt-ftparchive",
                ["release", Path.Combine(repoDir, "dists", "jammy")]);
            release = "Suite: jammy\nCodename: jammy\nOrigin: Test\nLabel: Test\n"
                + "Architectures: all amd64 i386\nComponents: main\nDescription: Test repo\n"
                + release;
            await File.WriteAllTextAsync(Path.Combine(repoDir, "dists", "jammy", "Release"), release);

            var repoUri = "file://" + repoDir;

            // ── 3. Create user postinst ──
            var scriptsDir = Path.Combine(projectDir, "scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "postinst.sh"),
                "if [ \"$1\" = \"configure\" ]; then\n"
                + "    echo \"my custom postinst ran\"\n"
                + "fi\n");
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "prerm.sh"),
                "if [ \"$1\" = \"remove\" ]; then\n"
                + "    echo \"my custom prerm ran\"\n"
                + "fi\n");
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "preinst.sh"),
                "echo \"my custom preinst ran\"\n");

            // ── 4. Build with SuppressUpstreamScripts=true ──
            var project = new AosprojProject
            {
                PackageName = "my-derived-pkg",
                PackageVersion = "2.0.0",
                PackageDescription = "Derived package with suppressed upstream scripts",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                TargetArchitectures = "all",
                UpstreamUrl = repoUri,
                UpstreamDistro = "ubuntu",
                UpstreamPackage = "fake-upstream",
                UpstreamSuite = "jammy",
                UpstreamComponent = "main",
                UpstreamArch = "all",
                SuppressUpstreamScripts = true,
                PostInstallScripts =
                {
                    new PostInstallScriptItem { Source = "scripts/postinst.sh" }
                },
                PreRemoveScripts =
                {
                    new PreRemoveScriptItem { Source = "scripts/prerm.sh" }
                },
                PreInstallScripts =
                {
                    new PreInstallScriptItem { Source = "scripts/preinst.sh" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "all", outputDir);

            // ── 5. Verify ──
            var staging = Path.Combine(projectDir, "obj", "jammy_all");
            var postinstContent = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "postinst"));

            Assert.IsFalse(postinstContent.Contains("upstream postinst ran"),
                "Upstream postinst content should NOT appear when SuppressUpstreamScripts=true.");
            Assert.IsFalse(postinstContent.Contains("exit 0"),
                "postinst should NOT contain exit 0 from upstream.");
            Assert.IsTrue(postinstContent.Contains("my custom postinst ran"),
                "Custom PostInstallScript should be present.");

            var prermContent = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "prerm"));
            Assert.IsTrue(prermContent.Contains("my custom prerm ran"),
                "Custom PreRemoveScript should be present.");

            var preinstContent = await File.ReadAllTextAsync(Path.Combine(staging, "DEBIAN", "preinst"));
            Assert.IsFalse(preinstContent.Contains("upstream preinst ran"),
                "Upstream preinst content should NOT appear when SuppressUpstreamScripts=true.");
            Assert.IsTrue(preinstContent.Contains("my custom preinst ran"),
                "Custom PreInstallScript should be present.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Symlink handling in IncludeFolder (CopyDirectory) ───────────────────

    [TestMethod]
    public async Task BuildAsync_IncludeFolder_FileSymlinksPreserved()
    {
        // File symlinks inside an IncludeFolder must be recreated as symlinks
        // in the staging directory, NOT expanded (dereferenced) to regular files.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Build a source tree: deploy/lib/libfoo.so (real) + deploy/lib/libfoo.so.1 -> libfoo.so (symlink)
            var deployDir = Path.Combine(projectDir, "deploy");
            var libDir = Path.Combine(deployDir, "lib");
            Directory.CreateDirectory(libDir);
            await File.WriteAllTextAsync(Path.Combine(libDir, "libfoo.so"), "ELF binary content");

            var symlinkPath = Path.Combine(libDir, "libfoo.so.1");
            var linkTarget = "libfoo.so"; // relative symlink
            File.CreateSymbolicLink(symlinkPath, linkTarget);

            Assert.IsTrue(File.Exists(symlinkPath), "Symlink should exist in source.");
            Assert.IsNotNull(new FileInfo(symlinkPath).LinkTarget, "Created file should be a symlink.");

            var project = new AosprojProject
            {
                PackageName = "symlink-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Symlink preservation test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFolders =
                {
                    new IncludeFolderItem { Source = "deploy/lib/", Target = "/usr/lib/symlink-pkg/" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            // Verify: libfoo.so is a regular file, libfoo.so.1 is a symlink
            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var destRealFile = Path.Combine(staging, "usr", "lib", "symlink-pkg", "libfoo.so");
            var destSymlink = Path.Combine(staging, "usr", "lib", "symlink-pkg", "libfoo.so.1");

            Assert.IsTrue(File.Exists(destRealFile), "Regular file should be copied.");
            Assert.IsTrue(File.Exists(destSymlink), "Symlink should exist in staging.");

            var actualTarget = new FileInfo(destSymlink).LinkTarget;
            Assert.AreEqual(linkTarget, actualTarget, "Symlink target must match the original relative target.");

            // The symlink target content should still resolve (proving it's a real symlink, not expanded)
            var realContent = await File.ReadAllTextAsync(destRealFile);
            var resolvedContent = await File.ReadAllTextAsync(destSymlink);
            Assert.AreEqual(realContent, resolvedContent, "Following the symlink should yield the same content.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_IncludeFolder_DirectorySymlinksPreservedNotRecursed()
    {
        // Directory symlinks inside an IncludeFolder must be recreated as directory
        // symlinks in staging and MUST NOT be recursed into (otherwise we'd
        // duplicate files under two paths, corrupting the .deb data.tar).
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Source tree:
            //   deploy/codecs/cs42l43-spk+cs35l56/init.conf       (real dir + file)
            //   deploy/codecs/cs35l56+cs42l43-spk -> cs42l43-spk+cs35l56  (dir symlink)
            var deployDir = Path.Combine(projectDir, "deploy");
            var codecsDir = Path.Combine(deployDir, "codecs");
            var realDir = Path.Combine(codecsDir, "cs42l43-spk+cs35l56");
            Directory.CreateDirectory(realDir);
            await File.WriteAllTextAsync(Path.Combine(realDir, "init.conf"), "# ALSA init config");

            var symlinkDir = Path.Combine(codecsDir, "cs35l56+cs42l43-spk");
            var dirLinkTarget = "cs42l43-spk+cs35l56"; // relative
            Directory.CreateSymbolicLink(symlinkDir, dirLinkTarget);

            Assert.IsTrue(Directory.Exists(symlinkDir), "Dir symlink should be traversable in source.");
            Assert.IsNotNull(new DirectoryInfo(symlinkDir).LinkTarget, "Created directory should be a symlink.");

            var project = new AosprojProject
            {
                PackageName = "dirsym-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Directory symlink test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFolders =
                {
                    new IncludeFolderItem { Source = "deploy/codecs/", Target = "/usr/share/dirsym-pkg/codecs/" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var codecsStaging = Path.Combine(staging, "usr", "share", "dirsym-pkg", "codecs");

            // Real dir + file should exist normally
            Assert.IsTrue(File.Exists(Path.Combine(codecsStaging, "cs42l43-spk+cs35l56", "init.conf")),
                "Real file under real dir should exist.");

            // The symlink dir should exist as a directory symlink (not a real dir)
            var destSymlinkDir = Path.Combine(codecsStaging, "cs35l56+cs42l43-spk");
            var dirInfo = new DirectoryInfo(destSymlinkDir);
            Assert.IsNotNull(dirInfo.LinkTarget, "Destination should be a directory symlink, not a real directory.");
            Assert.AreEqual(dirLinkTarget, dirInfo.LinkTarget, "Directory symlink target must match original.");

            // Verify the .deb archive has proper symlink entries and no duplicates.
            // dpkg-deb -c lists tar member types: '-' for regular file,
            // 'h' for hardlink, 'l' for symlink, 'd' for directory.
            var debPath = Path.Combine(outputDir, "dirsym-pkg_1.0.0_jammy_amd64.deb");
            Assert.IsTrue(File.Exists(debPath), ".deb should be produced.");
            var debContents = await RunAndCaptureAsync("dpkg-deb", ["-c", debPath]);

            // The real file must appear exactly once
            Assert.IsTrue(debContents.Contains("cs42l43-spk+cs35l56/init.conf"),
                "Real file path must be in the .deb listing.");
            // The symlink must appear as a symlink entry (tar shows "-> target")
            Assert.IsTrue(debContents.Contains("cs35l56+cs42l43-spk -> cs42l43-spk+cs35l56"),
                ".deb must contain the directory symlink entry.");
            // The real file must NOT also appear under the symlink path
            Assert.IsFalse(debContents.Contains("cs35l56+cs42l43-spk/init.conf"),
                "init.conf must NOT appear under the symlink path in the .deb — CopyDirectory must not recurse into directory symlinks.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_IncludeFolder_MixedRegularAndSymlinkContent()
    {
        // Comprehensive test: regular files, file symlinks, regular dirs,
        // and dir symlinks all coexist in a single IncludeFolder.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var deployDir = Path.Combine(projectDir, "deploy");
            var mixedDir = Path.Combine(deployDir, "mixed");
            Directory.CreateDirectory(mixedDir);

            // Regular file
            await File.WriteAllTextAsync(Path.Combine(mixedDir, "readme.txt"), "hello");

            // File symlink
            await File.WriteAllTextAsync(Path.Combine(mixedDir, "real.conf"), "# real config");
            File.CreateSymbolicLink(Path.Combine(mixedDir, "alias.conf"), "real.conf");

            // Regular directory with a file inside
            var nestedRealDir = Path.Combine(mixedDir, "plugins");
            Directory.CreateDirectory(nestedRealDir);
            await File.WriteAllTextAsync(Path.Combine(nestedRealDir, "plugin-a.so"), "plugin A");

            // Directory symlink (must NOT be recursed)
            Directory.CreateSymbolicLink(Path.Combine(mixedDir, "plugins-extra"), "plugins");

            var project = new AosprojProject
            {
                PackageName = "mixed-sym-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Mixed content symlink test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFolders =
                {
                    new IncludeFolderItem { Source = "deploy/mixed/", Target = "/usr/share/mixed/" }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var staging = Path.Combine(projectDir, "obj", "jammy_amd64");
            var m = Path.Combine(staging, "usr", "share", "mixed");

            // Regular file
            Assert.IsTrue(File.Exists(Path.Combine(m, "readme.txt")));
            Assert.AreEqual("hello", await File.ReadAllTextAsync(Path.Combine(m, "readme.txt")));

            // File symlink preserved
            var aliasInfo = new FileInfo(Path.Combine(m, "alias.conf"));
            Assert.IsNotNull(aliasInfo.LinkTarget, "alias.conf should be a symlink.");
            Assert.AreEqual("real.conf", aliasInfo.LinkTarget);

            // Regular nested dir
            Assert.IsTrue(File.Exists(Path.Combine(m, "plugins", "plugin-a.so")));

            // Dir symlink preserved (not recursed)
            var pluginsExtraInfo = new DirectoryInfo(Path.Combine(m, "plugins-extra"));
            Assert.IsNotNull(pluginsExtraInfo.LinkTarget, "plugins-extra should be a directory symlink.");
            Assert.AreEqual("plugins", pluginsExtraInfo.LinkTarget);

            // Verify no duplicates in the .deb: plugin-a.so must exist under
            // plugins/ but NOT under plugins-extra/ (which is a dir symlink).
            var debPath = Path.Combine(outputDir, "mixed-sym-pkg_1.0.0_jammy_amd64.deb");
            Assert.IsTrue(File.Exists(debPath), ".deb should be produced.");
            var debContents = await RunAndCaptureAsync("dpkg-deb", ["-c", debPath]);
            Assert.IsTrue(debContents.Contains("plugins/plugin-a.so"),
                "Real file must be in the .deb under the real directory path.");
            Assert.IsFalse(debContents.Contains("plugins-extra/plugin-a.so"),
                "File must NOT appear under the symlink path in the .deb.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task RunAsync(string command, string[] args, string? workingDir = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        var proc = System.Diagnostics.Process.Start(psi)!;
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"{command} exited {proc.ExitCode}: {err}");
        }
    }

    private static async Task<string> RunAndCaptureAsync(string command, string[] args, string? workingDir = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return stdout;
    }

    // ── Mode attribute support ─────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildAsync_IncludeFile_WithMode_AppliesPermissions()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "readme.txt"), "content");

            var project = new AosprojProject
            {
                PackageName = "mode-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Mode test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFiles =
                {
                    new IncludeFileItem
                    {
                        Source = "readme.txt",
                        Target = "/usr/share/doc/mode-pkg/readme.txt",
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                               UnixFileMode.GroupRead | UnixFileMode.OtherRead
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "usr", "share", "doc", "mode-pkg", "readme.txt");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupWrite));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherWrite));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherExecute));
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_IncludeFile_WithMode755()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "script.sh"), "#!/bin/sh\necho ok");

            var project = new AosprojProject
            {
                PackageName = "mode755-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Mode 755 test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFiles =
                {
                    new IncludeFileItem
                    {
                        Source = "script.sh",
                        Target = "/usr/bin/script",
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                               UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "usr", "bin", "script");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherExecute));
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_IncludeScript_WithCustomMode()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "helper.sh"), "#!/bin/sh\necho helper");

            var project = new AosprojProject
            {
                PackageName = "script-mode-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Script custom mode test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeScripts =
                {
                    new IncludeScriptItem
                    {
                        Source = "helper.sh",
                        Target = "/usr/lib/pkg/helper.sh",
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                               UnixFileMode.GroupRead | UnixFileMode.OtherRead
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "usr", "lib", "pkg", "helper.sh");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherExecute));
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_IncludeScript_WithoutMode_DefaultsTo0755()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "run.sh"), "#!/bin/sh\necho run");

            var project = new AosprojProject
            {
                PackageName = "default-mode-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Default mode test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeScripts =
                {
                    new IncludeScriptItem
                    {
                        Source = "run.sh",
                        Target = "/usr/bin/run"
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "usr", "bin", "run");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherExecute));
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_IncludeFile_WithoutMode_DefaultsTo0644()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Create a source file that is executable (0755) — the builder should
            // override it with the default 0644 since no explicit Mode is set.
            File.WriteAllText(Path.Combine(projectDir, "data.txt"), "data");
            var srcPath = Path.Combine(projectDir, "data.txt");
#pragma warning disable CA1416
            try { File.SetUnixFileMode(srcPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416

            var project = new AosprojProject
            {
                PackageName = "default-file-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "IncludeFile default mode test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                IncludeFiles =
                {
                    new IncludeFileItem
                    {
                        Source = "data.txt",
                        Target = "/usr/share/default-file-pkg/data.txt"
                        // No Mode set — should default to 0644
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "usr", "share", "default-file-pkg", "data.txt");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute), "IncludeFile without Mode should default to 0644 (not user-executable).");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupExecute), "IncludeFile without Mode should default to 0644 (not group-executable).");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherExecute), "IncludeFile without Mode should default to 0644 (not other-executable).");
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_ConfFile_WithoutMode_DefaultsTo0644()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Source file is executable — builder should override with 0644
            File.WriteAllText(Path.Combine(projectDir, "app.conf"), "key=value\n");
            var srcPath = Path.Combine(projectDir, "app.conf");
#pragma warning disable CA1416
            try { File.SetUnixFileMode(srcPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416

            var project = new AosprojProject
            {
                PackageName = "default-conf-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "ConfFile default mode test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                ConfFiles =
                {
                    new ConfFileItem
                    {
                        Source = "app.conf",
                        Target = "/etc/default-conf-pkg/app.conf"
                        // No Mode set — should default to 0644
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "etc", "default-conf-pkg", "app.conf");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute), "ConfFile without Mode should default to 0644.");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupExecute), "ConfFile without Mode should default to 0644.");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherExecute), "ConfFile without Mode should default to 0644.");
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_SystemdUnit_WithoutMode_DefaultsTo0644()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            // Source file is executable — builder should override with 0644
            var deployDir = Path.Combine(projectDir, "deploy");
            Directory.CreateDirectory(deployDir);
            await File.WriteAllTextAsync(Path.Combine(deployDir, "app.service"),
                "[Unit]\nDescription=App\n\n[Service]\nExecStart=/usr/bin/app\n\n[Install]\nWantedBy=multi-user.target\n");
            var srcPath = Path.Combine(deployDir, "app.service");
#pragma warning disable CA1416
            try { File.SetUnixFileMode(srcPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416

            var project = new AosprojProject
            {
                PackageName = "default-unit-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "SystemdUnit default mode test",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                SystemdUnits =
                {
                    new SystemdUnitItem
                    {
                        Source = "deploy/app.service",
                        AutoEnable = false
                        // No Mode set — should default to 0644
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "lib", "systemd", "system", "app.service");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserRead));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserWrite));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.UserExecute), "SystemdUnit without Mode should default to 0644.");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.GroupExecute), "SystemdUnit without Mode should default to 0644.");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherRead));
                Assert.IsFalse(mode.HasFlag(UnixFileMode.OtherExecute), "SystemdUnit without Mode should default to 0644.");
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_ConfFile_WithMode755_OverridesDefault()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "exec-config"), "#!/bin/sh\necho config-tool");

            var project = new AosprojProject
            {
                PackageName = "conf-mode755-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "ConfFile explicit Mode overrides default",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                ConfFiles =
                {
                    new ConfFileItem
                    {
                        Source = "exec-config",
                        Target = "/etc/conf-mode755-pkg/exec-config",
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                               UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "etc", "conf-mode755-pkg", "exec-config");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserExecute), "ConfFile with explicit Mode=755 should be executable.");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherExecute));
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [TestMethod]
    public async Task BuildAsync_SystemdUnit_WithMode755_OverridesDefault()
    {
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var deployDir = Path.Combine(projectDir, "deploy");
            Directory.CreateDirectory(deployDir);
            await File.WriteAllTextAsync(Path.Combine(deployDir, "special.service"),
                "[Unit]\nDescription=Special\n\n[Service]\nExecStart=/usr/bin/special\n");

            var project = new AosprojProject
            {
                PackageName = "unit-mode755-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "SystemdUnit explicit Mode overrides default",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                SystemdUnits =
                {
                    new SystemdUnitItem
                    {
                        Source = "deploy/special.service",
                        AutoEnable = false,
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                               UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                    }
                }
            };

            await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "amd64", outputDir);

            var dest = Path.Combine(projectDir, "obj", "jammy_amd64", "lib", "systemd", "system", "special.service");
            Assert.IsTrue(File.Exists(dest));

#pragma warning disable CA1416
            try
            {
                var mode = File.GetUnixFileMode(dest);
                Assert.IsTrue(mode.HasFlag(UnixFileMode.UserExecute), "SystemdUnit with explicit Mode=755 should be executable.");
                Assert.IsTrue(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.IsTrue(mode.HasFlag(UnixFileMode.OtherExecute));
            }
            catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    // ── UpstreamSignedBy ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildAsync_UpstreamSignedBy_MissingFile_Throws()
    {
        // The keyring file existence check happens before any apt interaction,
        // so we don't need a real upstream repo — just HasUpstreamSource=true.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var project = new AosprojProject
            {
                PackageName = "my-derived-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Derived package with missing keyring",
                Maintainer = "Test <test@example.com>",
                TargetSuites = "jammy",
                UpstreamPackage = "base-files",
                UpstreamUrl = "file:///nonexistent",
                UpstreamDistro = "ubuntu",
                UpstreamSuite = "jammy",
                UpstreamComponent = "main",
                UpstreamArch = "all",
                UpstreamSignedBy = "keys/nonexistent-keyring.gpg"
            };

            try
            {
                await _builder.BuildAsync(projectDir, project, "ubuntu", "jammy", "all", outputDir);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException ex)
            {
                Assert.IsTrue(ex.Message.Contains("UpstreamSignedBy"),
                    $"Exception should mention UpstreamSignedBy. Actual: {ex.Message}");
                Assert.IsTrue(ex.Message.Contains("not found"),
                    $"Exception should say file not found. Actual: {ex.Message}");
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Staging directory lifecycle / PrebuildCommand isolation ───────────────

    [TestMethod]
    public async Task BuildAsync_StagingDirs_LeftoverFromPreviousBuildAreVisible()
    {
        // Arrange: project with 2 suites, same arch. PrebuildCommand that
        // enumerates obj/* and writes the count + listing to a marker file.
        // Goal: prove that when building suite #2, the script sees both the
        // leftover staging dir from suite #1 AND the current staging dir.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var markerFile = Path.Combine(tempDir, "obj-dirs.txt");
            var shell = OperatingSystem.IsWindows()
                ? $"powershell -Command \"(Get-ChildItem -Directory {projectDir}/obj/* ^| Measure-Object).Count | Out-File -Encoding ASCII {markerFile}\""
                : $"echo $(ls -1d {projectDir}/obj/*/ 2>/dev/null | wc -l) > {markerFile}";

            var project = new AosprojProject
            {
                PackageName = "stale-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Stale staging dir test",
                Maintainer = "Test <test@example.com>",
                TargetDistro = "ubuntu",
                TargetSuites = "noble questing",
                PrebuildCommands =
                {
                    new PrebuildCommandItem { Run = shell }
                }
            };

            // Act: build first suite
            await _builder.BuildAsync(projectDir, project, "ubuntu", "noble", "amd64", outputDir);
            var countAfterFirst = int.Parse((await File.ReadAllTextAsync(markerFile)).Trim());
            Assert.AreEqual(1, countAfterFirst,
                "First build: PrebuildCommand should see exactly 1 staging dir (only noble).");

            // Assert: With the fix applied, stale staging dirs from previous
            // builds are cleaned up before the next build starts. So the
            // second build still sees exactly 1 staging dir (only its own).
            await _builder.BuildAsync(projectDir, project, "ubuntu", "questing", "amd64", outputDir);
            var countAfterSecond = int.Parse((await File.ReadAllTextAsync(markerFile)).Trim());
            Assert.AreEqual(1, countAfterSecond,
                "After fix: PrebuildCommand sees only its own staging dir. " +
                "Stale dirs from previous builds are cleaned up.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_PrebuildCommand_ReceivesAppropriateStageDirContext()
    {
        // Goal: demonstrate the fix — when APKG_STAGE_DIR is set as an
        // environment variable, PrebuildCommands can directly access the
        // correct staging directory without guessing via obj/* globs.
        // This test will PASS once the fix is applied.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var markerFile = Path.Combine(tempDir, "stage-dir.txt");
            var shell = OperatingSystem.IsWindows()
                ? $"powershell -Command \"$env:APKG_STAGE_DIR | Out-File -Encoding ASCII {markerFile}\""
                : $"echo $APKG_STAGE_DIR > {markerFile}";

            var project = new AosprojProject
            {
                PackageName = "ctx-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Stage dir context test",
                Maintainer = "Test <test@example.com>",
                TargetDistro = "ubuntu",
                TargetSuites = "noble questing",
                PrebuildCommands =
                {
                    new PrebuildCommandItem { Run = shell }
                }
            };

            // Act: build first suite
            await _builder.BuildAsync(projectDir, project, "ubuntu", "noble", "amd64", outputDir);
            var stageDir1 = (await File.ReadAllTextAsync(markerFile)).Trim();
            var expected1 = Path.Combine(projectDir, "obj", "noble_amd64");
            Assert.AreEqual(expected1, stageDir1,
                $"APKG_STAGE_DIR should point to the noble staging dir. Got: '{stageDir1}'");

            // Act: build second suite
            await _builder.BuildAsync(projectDir, project, "ubuntu", "questing", "amd64", outputDir);
            var stageDir2 = (await File.ReadAllTextAsync(markerFile)).Trim();
            var expected2 = Path.Combine(projectDir, "obj", "questing_amd64");
            Assert.AreEqual(expected2, stageDir2,
                $"APKG_STAGE_DIR should point to the questing staging dir. Got: '{stageDir2}'");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task BuildAsync_NoLeftoverStagingDirsAfterMultiSuiteBuild()
    {
        // Goal: after building all suites serially, only the LAST suite's
        // staging dir should remain (older ones cleaned up). The stale
        // accumulation bug is fixed.
        var tempDir = CreateTestDirectory();
        try
        {
            var projectDir = Path.Combine(tempDir, "project");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(projectDir);

            var project = new AosprojProject
            {
                PackageName = "clean-pkg",
                PackageVersion = "1.0.0",
                PackageDescription = "Cleanup test",
                Maintainer = "Test <test@example.com>",
                TargetDistro = "ubuntu",
                TargetSuites = "noble questing resolute",
            };

            // Act: build all 3 suites sequentially
            await _builder.BuildAsync(projectDir, project, "ubuntu", "noble", "amd64", outputDir);
            await _builder.BuildAsync(projectDir, project, "ubuntu", "questing", "amd64", outputDir);
            await _builder.BuildAsync(projectDir, project, "ubuntu", "resolute", "amd64", outputDir);

            // Assert: only the last suite's staging dir exists — no stale ones
            var objDir = Path.Combine(projectDir, "obj");
            if (Directory.Exists(objDir))
            {
                var remaining = Directory.GetDirectories(objDir);
                Assert.AreEqual(1, remaining.Length,
                    $"Expected exactly 1 staging dir (the last one built), but found: " +
                    string.Join(", ", remaining.Select(Path.GetFileName)));
                Assert.AreEqual("resolute_amd64", Path.GetFileName(remaining[0]),
                    "Only the most recent suite's staging dir should survive.");
            }
            // If obj/ doesn't exist at all, that's also fine (fully cleaned up)
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
