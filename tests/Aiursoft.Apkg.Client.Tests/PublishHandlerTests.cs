using Aiursoft.Apkg.Client.Handlers;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Client.Tests;

[TestClass]
public class PublishHandlerTests
{
    // ── ResolveBuildTargets ───────────────────────────────────────────────────

    [TestMethod]
    public void ResolveBuildTargets_All_CartesianProduct()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy noble",
            TargetArchitectures = "amd64 arm64"
        };

        var result = PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");

        Assert.AreEqual(4, result.Count);
        Assert.IsTrue(result.Contains(("ubuntu", "jammy", "amd64")));
        Assert.IsTrue(result.Contains(("ubuntu", "jammy", "arm64")));
        Assert.IsTrue(result.Contains(("ubuntu", "noble", "amd64")));
        Assert.IsTrue(result.Contains(("ubuntu", "noble", "arm64")));
    }

    [TestMethod]
    public void ResolveBuildTargets_NoSuiteArchArgs_FallsBackToAll()
    {
        var project = new AosprojProject
        {
            TargetDistro = "debian",
            TargetSuites = "bookworm",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(project, buildAll: false, "", "", "");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("debian", "bookworm", "amd64"), result[0]);
    }

    [TestMethod]
    public void ResolveBuildTargets_All_MissingTargetDistro_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("TargetDistro"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_All_MissingTargetSuites_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("TargetSuites"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_All_MissingTargetArchitectures_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = ""
        };

        try
        {
            PublishHandler.ResolveBuildTargets(project, buildAll: true, "", "", "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("TargetArchitectures"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_ExplicitSuiteAndArch_SingleTarget()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(
            project, buildAll: false, distroArg: "", suiteArg: "noble", archArg: "arm64");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("ubuntu", "noble", "arm64"), result[0]);
    }

    [TestMethod]
    public void ResolveBuildTargets_ExplicitSuite_MissingArch_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(
                project, buildAll: false, distroArg: "", suiteArg: "jammy", archArg: "");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("--arch"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_ExplicitArch_MissingSuite_Throws()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        try
        {
            PublishHandler.ResolveBuildTargets(
                project, buildAll: false, distroArg: "", suiteArg: "", archArg: "amd64");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("--suite"));
        }
    }

    [TestMethod]
    public void ResolveBuildTargets_DistroArgOverridesProject()
    {
        var project = new AosprojProject
        {
            TargetDistro = "ubuntu",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(
            project, buildAll: false, distroArg: "anduinos", suiteArg: "jammy", archArg: "amd64");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("anduinos", "jammy", "amd64"), result[0]);
    }

    [TestMethod]
    public void ResolveBuildTargets_DistroArgEmpty_ProjectDistroEmpty_FallsBackToUbuntu()
    {
        var project = new AosprojProject
        {
            TargetDistro = "",
            TargetSuites = "jammy",
            TargetArchitectures = "amd64"
        };

        var result = PublishHandler.ResolveBuildTargets(
            project, buildAll: false, distroArg: "", suiteArg: "jammy", archArg: "amd64");

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(("ubuntu", "jammy", "amd64"), result[0]);
    }

    // ── DeriveVersionFromDeb ──────────────────────────────────────────────────

    [TestMethod]
    public void DeriveVersionFromDeb_SimpleVersion()
    {
        var result = PublishHandler.DeriveVersionFromDeb(
            "/some/path/my-pkg_2.0.0_jammy_amd64.deb", "my-pkg");
        Assert.AreEqual("2.0.0", result);
    }

    [TestMethod]
    public void DeriveVersionFromDeb_UpstreamVersion()
    {
        var result = PublishHandler.DeriveVersionFromDeb(
            "base-files_13ubuntu10_jammy_amd64.deb", "base-files");
        Assert.AreEqual("13ubuntu10", result);
    }

    [TestMethod]
    public void DeriveVersionFromDeb_PackageNameWithUnderscores()
    {
        // Package name contains underscores but the version is between
        // the prefix (packageName + _) and the remaining _suite_arch
        var result = PublishHandler.DeriveVersionFromDeb(
            "some_pkg_1.2.3_noble_arm64.deb", "some_pkg");
        Assert.AreEqual("1.2.3", result);
    }

    [TestMethod]
    public void DeriveVersionFromDeb_JustFileName_NoDirectory()
    {
        var result = PublishHandler.DeriveVersionFromDeb(
            "pkg_3.4.5_plucky_amd64.deb", "pkg");
        Assert.AreEqual("3.4.5", result);
    }

    // ── ParseDebFileName ──────────────────────────────────────────────────────

    [TestMethod]
    public void ParseDebFileName_Basic()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("pkg_1.0_noble_amd64.deb");
        Assert.AreEqual("noble", suite);
        Assert.AreEqual("amd64", arch);
    }

    [TestMethod]
    public void ParseDebFileName_WithPathPrefix()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("/tmp/bin/pkg_2.0_plucky_arm64.deb");
        Assert.AreEqual("plucky", suite);
        Assert.AreEqual("arm64", arch);
    }

    [TestMethod]
    public void ParseDebFileName_SuiteWithHyphen()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("pkg_1.0_jammy-updates_amd64.deb");
        Assert.AreEqual("jammy-updates", suite);
        Assert.AreEqual("amd64", arch);
    }

    [TestMethod]
    public void ParseDebFileName_NoUnderscore_Throws()
    {
        try
        {
            PublishHandler.ParseDebFileName("pkg-amd64.deb");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("Cannot parse"));
        }
    }

    [TestMethod]
    public void ParseDebFileName_OnlyOneUnderscore_Throws()
    {
        try
        {
            PublishHandler.ParseDebFileName("pkg_amd64.deb");
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("Cannot parse"));
        }
    }

    [TestMethod]
    public void ParseDebFileName_NoExtension()
    {
        var (suite, arch) = PublishHandler.ParseDebFileName("mypkg_1.0_jammy_amd64.deb");
        Assert.AreEqual("jammy", suite);
        Assert.AreEqual("amd64", arch);
    }

    // ── DeriveStaticVersion ───────────────────────────────────────────────────

    [TestMethod]
    public void DeriveStaticVersion_PlainVersion_ReturnsAsIs()
    {
        Assert.AreEqual("1.0.3", PublishHandler.DeriveStaticVersion("1.0.3"));
    }

    [TestMethod]
    public void DeriveStaticVersion_TemplateWithPlusSeparator_StripsTemplate()
    {
        Assert.AreEqual("1.0.3", PublishHandler.DeriveStaticVersion("1.0.3+$(SuiteShortName)1"));
    }

    [TestMethod]
    public void DeriveStaticVersion_TemplateWithTildeSeparator_StripsTemplate()
    {
        Assert.AreEqual("1.0.3", PublishHandler.DeriveStaticVersion("1.0.3~$(SuiteShortName)"));
    }

    [TestMethod]
    public void DeriveStaticVersion_TemplateWithDotSeparator_StripsTemplate()
    {
        Assert.AreEqual("1.0.3", PublishHandler.DeriveStaticVersion("1.0.3.$(Build)"));
    }

    [TestMethod]
    public void DeriveStaticVersion_TemplateWithHyphenSeparator_StripsTemplate()
    {
        Assert.AreEqual("1.0.3", PublishHandler.DeriveStaticVersion("1.0.3-$(Rev)"));
    }

    [TestMethod]
    public void DeriveStaticVersion_EmptyString_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, PublishHandler.DeriveStaticVersion(""));
    }

    [TestMethod]
    public void DeriveStaticVersion_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, PublishHandler.DeriveStaticVersion("   "));
    }

    [TestMethod]
    public void DeriveStaticVersion_VersionStartsWithTemplate_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, PublishHandler.DeriveStaticVersion("$(Version)"));
    }

    [TestMethod]
    public void DeriveStaticVersion_MultipleSeparatorChars_AllTrimmed()
    {
        // e.g. "2.0.0+-$(Var)" → trim "+- " before "$(Var)" → "2.0.0"
        Assert.AreEqual("2.0.0", PublishHandler.DeriveStaticVersion("2.0.0+-$(Var)"));
    }


    [TestMethod]
    public void CollectDebFiles_MultipleVersionsInBin_AllVersionsCollected()
    {
        // Simulate bin/ containing stale .deb files from multiple past builds.
        // The current pack step uses Directory.GetFiles(binDir, "{name}_*.deb")
        // with no version filter, so all versions get packed into the .apkg.
        var tmp = Path.Combine(Path.GetTempPath(), $"apkg_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.1_noble_amd64.deb"), "");
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.1_noble_arm64.deb"), "");
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.2_noble_amd64.deb"), "");
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.2_noble_arm64.deb"), "");
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.3_noble_amd64.deb"), "");
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.3_noble_arm64.deb"), "");

            // This is exactly what PublishHandler does at line 145.
            var collected = Directory.GetFiles(tmp, "myapp_*.deb");

            // BUG: all 6 files are collected — 1.0.1, 1.0.2, and 1.0.3 all together.
            // A single .apkg would contain every .deb ever built, regardless of version.
            Assert.AreEqual(6, collected.Length,
                "All versions are collected — old .deb files contaminate the .apkg");

            // BUG: debFiles[0] is filesystem-order-dependent. We can't predict which
            // version "wins", but it's a fact that the code picks the first one blindly.
            // The right behaviour would be to derive the version from the project, or to
            // clean up old files so only one version exists at pack time.
            var derived = PublishHandler.DeriveVersionFromDeb(collected[0], "myapp");
            var allVersions = new[] { "1.0.1", "1.0.2", "1.0.3" };
            Assert.IsTrue(allVersions.Contains(derived),
                $"Derived version '{derived}' is one of the versions on disk — " +
                "but which one is a matter of luck.");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [TestMethod]
    public void CollectDebFiles_OnlyOneVersionInBin_AllGood()
    {
        // When bin/ has only the current version's files, everything works.
        var tmp = Path.Combine(Path.GetTempPath(), $"apkg_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.3_noble_amd64.deb"), "");
            File.WriteAllText(Path.Combine(tmp, "myapp_1.0.3_noble_arm64.deb"), "");

            var collected = Directory.GetFiles(tmp, "myapp_*.deb");

            Assert.AreEqual(2, collected.Length);
            var derived = PublishHandler.DeriveVersionFromDeb(collected[0], "myapp");
            Assert.AreEqual("1.0.3", derived);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
