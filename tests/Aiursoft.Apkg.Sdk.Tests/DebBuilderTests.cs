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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", ["libc6", "libssl3 (>= 3.0)"]);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);

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
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", merged, upstreamControl);

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
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", merged, upstreamControl);

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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], upstreamControl);
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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], upstreamControl);
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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], upstreamControl);
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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], upstreamControl);
        Assert.IsTrue(control.Contains("Provides: upstream-virtual-pkg"));
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

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], upstreamControl);
        Assert.IsTrue(control.Contains("Conflicts: old-pkg"));
        Assert.IsTrue(control.Contains("Replaces: old-pkg"));
    }

    [TestMethod]
    public void BuildControl_NoUpstream_NoSectionPriority()
    {
        var project = new AosprojProject
        {
            PackageName = "my-pkg",
            PackageVersion = "1.0",
            PackageDescription = "desc"
        };

        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", []);
        Assert.IsFalse(control.Contains("Section:"));
        Assert.IsFalse(control.Contains("Priority:"));
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
        var control = DebBuilder.BuildControl(project, resolvedVersion, "amd64", [], upstreamControl);

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

        var result = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", [], upstreamControl);
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
        var control = DebBuilder.BuildControl(project, project.PackageVersion, "amd64", merged, upstreamControl);
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

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "deb-builder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
