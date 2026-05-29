using Aiursoft.Apkg.Services;


namespace Aiursoft.Apkg.WebTests;

/// <summary>
/// Unit tests for AptVersionComparisonService.
/// All expected results are verified against dpkg --compare-versions on Ubuntu.
/// </summary>
[TestClass]
public class AptVersionComparisonServiceTests
{
    private readonly AptVersionComparisonService _svc = new();

    // ===================================================================
    // Basic numeric comparison
    // ===================================================================

    [TestMethod]
    public void Compare_SimpleNumeric_GreaterVersion()
    {
        Assert.IsTrue(_svc.Compare("2.0", "1.0") > 0);
    }

    [TestMethod]
    public void Compare_SimpleNumeric_LesserVersion()
    {
        Assert.IsTrue(_svc.Compare("1.0", "2.0") < 0);
    }

    [TestMethod]
    public void Compare_SimpleNumeric_Equal()
    {
        Assert.AreEqual(0, _svc.Compare("1.0", "1.0"));
    }

    [TestMethod]
    public void Compare_MultiDigitNumeric_15_vs_4()
    {
        // dpkg: 15 > 4
        Assert.IsTrue(_svc.Compare("15", "4") > 0);
    }

    [TestMethod]
    public void Compare_MultiDigitNumeric_21_vs_6()
    {
        // dpkg: 1.21 > 1.6
        Assert.IsTrue(_svc.Compare("1.21", "1.6") > 0);
    }

    [TestMethod]
    public void Compare_MultiDigitNumeric_100_vs_99()
    {
        Assert.IsTrue(_svc.Compare("1.100", "1.99") > 0);
    }

    // ===================================================================
    // Epoch handling
    // ===================================================================

    [TestMethod]
    public void Compare_Epoch_HigherEpochWins()
    {
        // dpkg: 1:1.0 > 2.0 (epoch 1 > epoch 0)
        Assert.IsTrue(_svc.Compare("1:1.0", "2.0") > 0);
    }

    [TestMethod]
    public void Compare_Epoch_SameEpoch()
    {
        Assert.IsTrue(_svc.Compare("1:2.0", "1:1.0") > 0);
    }

    [TestMethod]
    public void Compare_Epoch_EqualVersions()
    {
        Assert.AreEqual(0, _svc.Compare("1:1.0", "1:1.0"));
    }

    [TestMethod]
    public void Compare_ComplexEpoch_Self()
    {
        // dpkg: 1:4.16.0-2+really2.41-4ubuntu4 == 1:4.16.0-2+really2.41-4ubuntu4
        Assert.AreEqual(0, _svc.Compare("1:4.16.0-2+really2.41-4ubuntu4", "1:4.16.0-2+really2.41-4ubuntu4"));
    }

    // ===================================================================
    // Tilde (~) handling — sorts before everything including end-of-string
    // ===================================================================

    [TestMethod]
    public void Compare_Tilde_SortsBeforeEndOfString()
    {
        // dpkg: 1.0~beta1 < 1.0
        Assert.IsTrue(_svc.Compare("1.0~beta1", "1.0") < 0);
    }

    [TestMethod]
    public void Compare_Tilde_ComparesWithinTilde()
    {
        // dpkg: 1.0~beta1 < 1.0~beta2
        Assert.IsTrue(_svc.Compare("1.0~beta1", "1.0~beta2") < 0);
    }

    [TestMethod]
    public void Compare_Tilde_InConstraint()
    {
        // dpkg: 7.0.0-really-5.3.5-6 >= 4~
        Assert.IsTrue(_svc.SatisfiesConstraint("7.0.0-really-5.3.5-6", ">= 4~"));
    }

    [TestMethod]
    public void Compare_Tilde_InConstraint2()
    {
        // dpkg: 7.0.0-really-5.3.5-6 >= 4.3.2~
        Assert.IsTrue(_svc.SatisfiesConstraint("7.0.0-really-5.3.5-6", ">= 4.3.2~"));
    }

    // ===================================================================
    // Hyphenated upstream versions (last hyphen separates revision)
    // ===================================================================

    [TestMethod]
    public void Compare_HyphenatedUpstream_MultipleHyphens()
    {
        // dpkg: 3.1-20250104-1build1 >= 3.1-20140620-0
        // Parsed as upstream=3.1-20250104, revision=1build1 vs upstream=3.1-20140620, revision=0
        Assert.IsTrue(_svc.SatisfiesConstraint("3.1-20250104-1build1", ">= 3.1-20140620-0"));
    }

    [TestMethod]
    public void Compare_HyphenatedUpstream_StableRevision()
    {
        // dpkg: 2.1.12-stable-10build1 >= 2.1.8-stable
        Assert.IsTrue(_svc.SatisfiesConstraint("2.1.12-stable-10build1", ">= 2.1.8-stable"));
    }

    [TestMethod]
    public void Compare_HyphenatedUpstream_DashInBothParts()
    {
        // dpkg: 0.6-30-1 >= 0.6-26
        Assert.IsTrue(_svc.SatisfiesConstraint("0.6-30-1", ">= 0.6-26"));
    }

    [TestMethod]
    public void Compare_HyphenatedUpstream_MoreExamples()
    {
        // dpkg: 1.7-3-1 >= 1.5-0
        Assert.IsTrue(_svc.SatisfiesConstraint("1.7-3-1", ">= 1.5-0"));
        // dpkg: 1.7-3-1 >= 1.2-6
        Assert.IsTrue(_svc.SatisfiesConstraint("1.7-3-1", ">= 1.2-6"));
    }

    [TestMethod]
    public void Compare_HyphenatedUpstream_DashNumberDash()
    {
        // dpkg: 1.48.0-2-3 = 1.48.0-2-3
        Assert.AreEqual(0, _svc.Compare("1.48.0-2-3", "1.48.0-2-3"));
        // dpkg: 1.48.0-2-3 >= 1.48.0-2
        Assert.IsTrue(_svc.SatisfiesConstraint("1.48.0-2-3", ">= 1.48.0-2"));
    }

    [TestMethod]
    public void Compare_HyphenatedUpstream_ReallyVersion()
    {
        // dpkg: 7.0.0-really-5.3.5-6 >= 4~
        Assert.IsTrue(_svc.SatisfiesConstraint("7.0.0-really-5.3.5-6", ">= 4~"));
    }

    // ===================================================================
    // dfsg and special upstream suffixes
    // ===================================================================

    [TestMethod]
    public void Compare_Dfsg_HigherMajor()
    {
        // dpkg: 1.21.3 > 1.6.dfsg.2 (letters sort after digits exit the non-digit comparison)
        Assert.IsTrue(_svc.Compare("1.21.3", "1.6.dfsg.2") > 0);
    }

    [TestMethod]
    public void Compare_Dfsg_InConstraint()
    {
        // dpkg: 1.21.3-5ubuntu2 >= 1.6.dfsg.2
        Assert.IsTrue(_svc.SatisfiesConstraint("1.21.3-5ubuntu2", ">= 1.6.dfsg.2"));
    }

    // ===================================================================
    // Real-world version comparisons from the error log
    // ===================================================================

    [TestMethod]
    public void Compare_RealWorld_LibgccS1()
    {
        // dpkg: 15.2.0-4ubuntu4 >= 4.2
        Assert.IsTrue(_svc.SatisfiesConstraint("15.2.0-4ubuntu4", ">= 4.2"));
    }

    [TestMethod]
    public void Compare_RealWorld_LibgccS1_331()
    {
        // dpkg: 15.2.0-4ubuntu4 >= 3.3.1
        Assert.IsTrue(_svc.SatisfiesConstraint("15.2.0-4ubuntu4", ">= 3.3.1"));
    }

    [TestMethod]
    public void Compare_RealWorld_GnuPg()
    {
        // 2.4.8-2ubuntu2 >= 2.4.8-2ubuntu2 (True)
        Assert.IsTrue(_svc.SatisfiesConstraint("2.4.8-2ubuntu2", ">= 2.4.8-2ubuntu2"));
        // 2.4.8-2ubuntu2.1 >= 2.4.8-2ubuntu2 (True)
        Assert.IsTrue(_svc.SatisfiesConstraint("2.4.8-2ubuntu2.1", ">= 2.4.8-2ubuntu2"));
        // 2.2.46 < 2.4.8-2ubuntu2 (False)
        Assert.IsFalse(_svc.SatisfiesConstraint("2.2.46", ">= 2.4.8-2ubuntu2"));
    }

    [TestMethod]
    public void Compare_RealWorld_SysvinitUtils()
    {
        // dpkg: 3.14-4ubuntu1 >= 3.05-4
        Assert.IsTrue(_svc.SatisfiesConstraint("3.14-4ubuntu1", ">= 3.05-4"));
    }

    [TestMethod]
    public void Compare_RealWorld_LibgssapiKrb5()
    {
        // dpkg: 1.21.3-5ubuntu2 >= 1.6.dfsg.2
        Assert.IsTrue(_svc.SatisfiesConstraint("1.21.3-5ubuntu2", ">= 1.6.dfsg.2"));
    }

    [TestMethod]
    public void Compare_RealWorld_Libldap2()
    {
        // dpkg: 2.6.10+dfsg-1ubuntu2 >= 2.6.2
        Assert.IsTrue(_svc.SatisfiesConstraint("2.6.10+dfsg-1ubuntu2", ">= 2.6.2"));
    }

    [TestMethod]
    public void Compare_RealWorld_Libnettle()
    {
        // dpkg: 3.10.1-1 >= 3.8
        Assert.IsTrue(_svc.SatisfiesConstraint("3.10.1-1", ">= 3.8"));
    }

    [TestMethod]
    public void Compare_RealWorld_Texlive()
    {
        // dpkg: 3.1-20250104-1build1 >= 2.11-20080614-0
        Assert.IsTrue(_svc.SatisfiesConstraint("3.1-20250104-1build1", ">= 2.11-20080614-0"));
    }

    [TestMethod]
    public void Compare_RealWorld_Libevent()
    {
        // dpkg: 2.1.12-stable-10build1 >= 2.1.8-stable
        Assert.IsTrue(_svc.SatisfiesConstraint("2.1.12-stable-10build1", ">= 2.1.8-stable"));
    }

    [TestMethod]
    public void Compare_RealWorld_Isc_Dhcp()
    {
        // dpkg: 1:4.4.1-p3+ds-1 >= 1:4.4.1-p3+ds
        Assert.IsTrue(_svc.SatisfiesConstraint("1:4.4.1-p3+ds-1", ">= 1:4.4.1-p3+ds"));
    }

    [TestMethod]
    public void Compare_RealWorld_Timezone()
    {
        // dpkg: 21-361-2build1 >= 21-361
        Assert.IsTrue(_svc.SatisfiesConstraint("21-361-2build1", ">= 21-361"));
    }

    [TestMethod]
    public void Compare_RealWorld_Evince()
    {
        // dpkg: 0.25.1-gtk3+dfsg-1 >= 0.25.1-gtk3+dfsg
        Assert.IsTrue(_svc.SatisfiesConstraint("0.25.1-gtk3+dfsg-1", ">= 0.25.1-gtk3+dfsg"));
    }

    [TestMethod]
    public void Compare_RealWorld_Java()
    {
        // dpkg: 8u462-ga-1 = 8u462-ga-1
        Assert.AreEqual(0, _svc.Compare("8u462-ga-1", "8u462-ga-1"));
        Assert.IsTrue(_svc.SatisfiesConstraint("8u462-ga-1", "= 8u462-ga-1"));
    }

    [TestMethod]
    public void Compare_RealWorld_GitRepos()
    {
        // dpkg: 4.13.0+git99-gc5587f9-1 >= 4.9
        Assert.IsTrue(_svc.SatisfiesConstraint("4.13.0+git99-gc5587f9-1", ">= 4.9"));
    }

    [TestMethod]
    public void Compare_RealWorld_Lts()
    {
        // dpkg: 20190301-lts1-5 >= 20161115
        Assert.IsTrue(_svc.SatisfiesConstraint("20190301-lts1-5", ">= 20161115"));
    }

    [TestMethod]
    public void Compare_RealWorld_Openmpt()
    {
        // dpkg: 0.8.9.0-openmpt1-2build2 >= 0.2.7386~beta20.3
        Assert.IsTrue(_svc.SatisfiesConstraint("0.8.9.0-openmpt1-2build2", ">= 0.2.7386~beta20.3"));
    }

    [TestMethod]
    public void Compare_RealWorld_PulseAudio()
    {
        // dpkg: 4.20.0+68-g35cb38b222-1 >= 4.20.0+68-g35cb38b222
        Assert.IsTrue(_svc.SatisfiesConstraint("4.20.0+68-g35cb38b222-1", ">= 4.20.0+68-g35cb38b222"));
    }

    [TestMethod]
    public void Compare_RealWorld_PulseAudio_OlderConstraint()
    {
        // dpkg: 4.20.0+68-g35cb38b222-1 >= 4.16.0
        Assert.IsTrue(_svc.SatisfiesConstraint("4.20.0+68-g35cb38b222-1", ">= 4.16.0"));
    }

    [TestMethod]
    public void Compare_RealWorld_RVersion()
    {
        // dpkg: 3.99-0.18-1build1 >= 1.98-0
        Assert.IsTrue(_svc.SatisfiesConstraint("3.99-0.18-1build1", ">= 1.98-0"));
        // dpkg: 3.99-0.18-1build1 >= 3.98-1.3
        Assert.IsTrue(_svc.SatisfiesConstraint("3.99-0.18-1build1", ">= 3.98-1.3"));
    }

    [TestMethod]
    public void Compare_RealWorld_SvnVersion()
    {
        // dpkg: 1:2.6.0~svn-r3005-6build2 = 1:2.6.0~svn-r3005-6build2
        Assert.AreEqual(0, _svc.Compare("1:2.6.0~svn-r3005-6build2", "1:2.6.0~svn-r3005-6build2"));
    }

    [TestMethod]
    public void Compare_RealWorld_GitDate()
    {
        // dpkg: 0.0.0-git20150806-10 >= 0.0.0-git20150806
        Assert.IsTrue(_svc.SatisfiesConstraint("0.0.0-git20150806-10", ">= 0.0.0-git20150806"));
    }

    [TestMethod]
    public void Compare_RealWorld_Rc5()
    {
        // dpkg: 0.0.12-rc5+git20230513+5581005-1 >= 0.0.12-rc5
        Assert.IsTrue(_svc.SatisfiesConstraint("0.0.12-rc5+git20230513+5581005-1", ">= 0.0.12-rc5"));
    }

    [TestMethod]
    public void Compare_RealWorld_Grub()
    {
        // dpkg: 1.27-3-3.1 >= 1.4-1-1
        Assert.IsTrue(_svc.SatisfiesConstraint("1.27-3-3.1", ">= 1.4-1-1"));
    }

    // ===================================================================
    // SatisfiesConstraint operator tests
    // ===================================================================

    [TestMethod]
    public void SatisfiesConstraint_StrictlyLess()
    {
        Assert.IsTrue(_svc.SatisfiesConstraint("1.0", "<< 2.0"));
        Assert.IsFalse(_svc.SatisfiesConstraint("2.0", "<< 2.0"));
        Assert.IsFalse(_svc.SatisfiesConstraint("3.0", "<< 2.0"));
    }

    [TestMethod]
    public void SatisfiesConstraint_LessOrEqual()
    {
        Assert.IsTrue(_svc.SatisfiesConstraint("1.0", "<= 2.0"));
        Assert.IsTrue(_svc.SatisfiesConstraint("2.0", "<= 2.0"));
        Assert.IsFalse(_svc.SatisfiesConstraint("3.0", "<= 2.0"));
    }

    [TestMethod]
    public void SatisfiesConstraint_Equal()
    {
        Assert.IsTrue(_svc.SatisfiesConstraint("2.0", "= 2.0"));
        Assert.IsFalse(_svc.SatisfiesConstraint("1.0", "= 2.0"));
    }

    [TestMethod]
    public void SatisfiesConstraint_GreaterOrEqual()
    {
        Assert.IsTrue(_svc.SatisfiesConstraint("3.0", ">= 2.0"));
        Assert.IsTrue(_svc.SatisfiesConstraint("2.0", ">= 2.0"));
        Assert.IsFalse(_svc.SatisfiesConstraint("1.0", ">= 2.0"));
    }

    [TestMethod]
    public void SatisfiesConstraint_StrictlyGreater()
    {
        Assert.IsTrue(_svc.SatisfiesConstraint("3.0", ">> 2.0"));
        Assert.IsFalse(_svc.SatisfiesConstraint("2.0", ">> 2.0"));
        Assert.IsFalse(_svc.SatisfiesConstraint("1.0", ">> 2.0"));
    }

    [TestMethod]
    public void SatisfiesConstraint_NoConstraint()
    {
        // Single word with no operator should always satisfy
        Assert.IsTrue(_svc.SatisfiesConstraint("1.0", "anything"));
    }

    // ===================================================================
    // Edge cases: leading zeros
    // ===================================================================

    [TestMethod]
    public void Compare_LeadingZeros_AreIgnored()
    {
        // dpkg: 01 == 1 numerically
        Assert.AreEqual(0, _svc.Compare("01", "1"));
        Assert.AreEqual(0, _svc.Compare("001.002", "1.2"));
    }

    // ===================================================================
    // Edge cases: plus sign in versions
    // ===================================================================

    [TestMethod]
    public void Compare_PlusSign()
    {
        // + is a non-letter, non-digit char; sorts after letters
        Assert.IsTrue(_svc.Compare("2.6.10+dfsg-1", "2.6.2-1") > 0);
    }

    // ===================================================================
    // Revision comparison
    // ===================================================================

    [TestMethod]
    public void Compare_Revision_DiffersOnly()
    {
        Assert.IsTrue(_svc.Compare("1.0-2", "1.0-1") > 0);
        Assert.IsTrue(_svc.Compare("1.0-1", "1.0-2") < 0);
        Assert.AreEqual(0, _svc.Compare("1.0-1", "1.0-1"));
    }

    [TestMethod]
    public void Compare_Revision_NoRevisionVsRevision()
    {
        // No revision defaults to "0", which is less than "1"
        Assert.IsTrue(_svc.Compare("1.0-1", "1.0") > 0);
    }

    // ===================================================================
    // Tilde edge cases — ~ sorts BEFORE everything
    // ===================================================================

    [TestMethod]
    public void Compare_Tilde_SortsBeforeAnyLetter()
    {
        // dpkg: 1.0~a < 1.0a (tilde before letter)
        Assert.IsTrue(_svc.Compare("1.0~a", "1.0a") < 0);
    }

    [TestMethod]
    public void Compare_Tilde_SortsBeforeDigit()
    {
        // dpkg: 1.0~1 < 1.01 (tilde before digit)
        Assert.IsTrue(_svc.Compare("1.0~1", "1.01") < 0);
    }

    [TestMethod]
    public void Compare_Tilde_SortsBeforeNothing()
    {
        // dpkg: 1.0~ < 1.0 (tilde without suffix still sorts before end-of-string)
        Assert.IsTrue(_svc.Compare("1.0~", "1.0") < 0);
    }

    [TestMethod]
    public void Compare_Tilde_SortsBeforeEndOfString2()
    {
        // dpkg: 1.0~rc1 < 1.0
        Assert.IsTrue(_svc.Compare("1.0~rc1", "1.0") < 0);
    }

    [TestMethod]
    public void Compare_Tilde_MultipleTildes()
    {
        // dpkg: 1.0~~a < 1.0~a (more tildes = older)
        Assert.IsTrue(_svc.Compare("1.0~~a", "1.0~a") < 0);
    }

    // ===================================================================
    // Plus sign — sorts AFTER letters (ASCII + 256 for non-alnum)
    // ===================================================================

    [TestMethod]
    public void Compare_Plus_SortsAfterLetter()
    {
        // dpkg: 1.0+a > 1.0a (plus after letter)
        Assert.IsTrue(_svc.Compare("1.0+a", "1.0a") > 0);
    }

    [TestMethod]
    public void Compare_Plus_SortsAfterEndOfString()
    {
        // dpkg: 1.0+anything > 1.0 (+ has order 43+256=299, empty string = 0)
        Assert.IsTrue(_svc.Compare("1.0+dfsg", "1.0") > 0);
    }

    [TestMethod]
    public void Compare_Plus_UbuntuNigCom()
    {
        // dpkg: 1.0+nmu1 > 1.0 (non-maintainer upload suffix)
        Assert.IsTrue(_svc.Compare("1.0+nmu1", "1.0") > 0);
    }

    // ===================================================================
    // Letter vs digit at same position
    // ===================================================================

    [TestMethod]
    public void Compare_DigitSortsBeforeLetter()
    {
        // dpkg: 1.0a > 1.0 (digit '0' then end gives way to letter 'a')
        // Wait: "1.0" vs "1.0a" → both share "1.", then "0" vs "0" (equal digits),
        // then "a" vs end-of-string (letter > 0), so "1.0a" > "1.0"
        Assert.IsTrue(_svc.Compare("1.0a", "1.0") > 0);
    }

    [TestMethod]
    public void Compare_LetterSequence()
    {
        // dpkg: 1.0a < 1.0b (lexicographic)
        Assert.IsTrue(_svc.Compare("1.0a", "1.0b") < 0);
    }

    [TestMethod]
    public void Compare_DigitAtSamePosition()
    {
        // dpkg: "1.0-a" > "1.0-2" because 'a' (letter, order=97) > '2' (digit, order=0)
        Assert.IsTrue(_svc.Compare("1.0-a", "1.0-2") > 0);
    }

    // ===================================================================
    // Revision parsing: only the LAST hyphen separates revision
    // ===================================================================

    [TestMethod]
    public void Compare_Revision_OnlyLastHyphenSeparates()
    {
        // "1.0-2-3" → upstream="1.0-2", revision="3"
        // "1.0-2"   → upstream="1.0",   revision="2"
        // Upstream: "1.0-2" vs "1.0" → at hyphen: '1' vs '\0'? Let me trace:
        // "1.0-2" vs "1.0": after "1.0", side A has "-2", side B has "".
        // '-' is non-alnum char, order = '-' (45) + 256 = 301 > 0, so "1.0-2" > "1.0"
        // So upstream "1.0-2" > "1.0", meaning "1.0-2-3" > "1.0-2"
        Assert.IsTrue(_svc.Compare("1.0-2-3", "1.0-2") > 0);
    }

    [TestMethod]
    public void Compare_Revision_WrongHyphenParsing()
    {
        // "3.1-20250104-1build1":
        //   upstream="3.1-20250104", revision="1build1"
        // "3.1-20140620-0":
        //   upstream="3.1-20140620", revision="0"
        // Upstream: 20250104 > 20140620, so "3.1-20250104-1build1" > "3.1-20140620-0"
        Assert.IsTrue(_svc.Compare("3.1-20250104-1build1", "3.1-20140620-0") > 0);
    }

    [TestMethod]
    public void Compare_Revision_UbuntuDeb()
    {
        // "2.1.12-stable-10build1": upstream="2.1.12-stable", revision="10build1"
        // "2.1.8-stable":           upstream="2.1.8",         revision="stable"
        // Upstream: "2.1.12-stable" vs "2.1.8": digits 12 > 8, so former > latter
        Assert.IsTrue(_svc.Compare("2.1.12-stable-10build1", "2.1.8-stable") > 0);
    }

    // ===================================================================
    // Character ordering: ~ < end-of-string < digits < letters < other
    // ===================================================================

    [TestMethod]
    public void Compare_CharOrder_EmptyBeforeDigit()
    {
        // dpkg: 1.0 < 1.01 (at position after '.': "" vs "0" → 0 vs 0, then "1" vs "" → 1 > 0)
        // So "1.01" > "1.0": longer digit = larger
        Assert.IsTrue(_svc.Compare("1.01", "1.0") > 0);
    }

    [TestMethod]
    public void Compare_CharOrder_DotBetween()
    {
        // dpkg: 1.0.0 > 1.0 (extra dot + 0)
        Assert.IsTrue(_svc.Compare("1.0.0", "1.0") > 0);
    }

    [TestMethod]
    public void Compare_CharOrder_DotVsLetter()
    {
        // '.' (46+256=302) > 'a' (97), so 1.0.0 > 1.0a
        Assert.IsTrue(_svc.Compare("1.0.0", "1.0a") > 0);
    }

    // ===================================================================
    // Epoch edge cases
    // ===================================================================

    [TestMethod]
    public void Compare_Epoch_ZeroVsNoEpoch()
    {
        // dpkg: 0:1.0 == 1.0 (explicit epoch 0 equals implicit epoch 0)
        Assert.AreEqual(0, _svc.Compare("0:1.0", "1.0"));
    }

    [TestMethod]
    public void Compare_Epoch_Large()
    {
        // dpkg: 99:1.0 > 98:99.99
        Assert.IsTrue(_svc.Compare("99:1.0", "98:99.99") > 0);
    }

    // ===================================================================
    // Real-world version sort test — sort a list and verify order
    // ===================================================================

    [TestMethod]
    public void Sort_RealWorldList_GnomeExtensions()
    {
        var versions = new[]
        {
            "69.0",
            "69.1",
            "69.2",
            "1.0.69",
            "1.0.70",
            "1.0.71",
        };
        var sorted = versions.OrderBy(v => v, Comparer<string>.Create(_svc.Compare)).ToList();
        // Expected: 1.0.69, 1.0.70, 1.0.71, 69.0, 69.1, 69.2
        // (1 < 69 at first numeric segment)
        Assert.AreEqual("1.0.69", sorted[0]);
        Assert.AreEqual("1.0.70", sorted[1]);
        Assert.AreEqual("1.0.71", sorted[2]);
        Assert.AreEqual("69.0", sorted[3]);
        Assert.AreEqual("69.1", sorted[4]);
        Assert.AreEqual("69.2", sorted[5]);
    }

    [TestMethod]
    public void Sort_RealWorldList_BaseFiles()
    {
        var versions = new[]
        {
            "1:14ubuntu6-anduinos",           // May 28
            "1:13ubuntu10+noble-addon1-anduinos", // May 29 (newer upload, older version)
        };
        var sorted = versions.OrderBy(v => v, Comparer<string>.Create(_svc.Compare)).ToList();
        // Epoch both 1. Upstream: "13ubuntu10+noble-addon1" vs "14ubuntu6"
        // 13 < 14 (by numeric comparison), so 1:13... < 1:14...
        Assert.AreEqual("1:13ubuntu10+noble-addon1-anduinos", sorted[0]);
        Assert.AreEqual("1:14ubuntu6-anduinos", sorted[1]);
    }

    [TestMethod]
    public void Sort_Reverse_BaseFiles_LatestFirst()
    {
        var versions = new[]
        {
            "1:13ubuntu10+noble-addon1-anduinos",
            "1:14ubuntu6-anduinos",
        };
        var sorted = versions.OrderByDescending(v => v, Comparer<string>.Create(_svc.Compare)).ToList();
        // Latest (by Debian rules) first: "1:14ubuntu6-anduinos"
        Assert.AreEqual("1:14ubuntu6-anduinos", sorted[0]);
    }

    // ===================================================================
    // Leading zeros
    // ===================================================================

    [TestMethod]
    public void Compare_LeadingZeros_Padding()
    {
        // dpkg: 0001.0002 == 1.2
        Assert.AreEqual(0, _svc.Compare("0001.0002", "1.2"));
    }

    [TestMethod]
    public void Compare_LeadingZeros_BeforeNumber()
    {
        // dpkg: 1.0-0ubuntu1 vs 1.0-ubuntu1 → "0ubuntu1" vs "ubuntu1": digit '0' vs letter 'u'
        // '0' digit (0) vs 'u' letter (117), so 'u' > '0', so "ubuntu1" > "0ubuntu1"
        Assert.IsTrue(_svc.Compare("1.0-ubuntu1", "1.0-0ubuntu1") > 0);
    }

    // ===================================================================
    // Constraint satisfaction edge cases
    // ===================================================================

    [TestMethod]
    public void SatisfiesConstraint_TildeVersions()
    {
        // 1.0~rc1 does NOT satisfy >= 1.0 (since ~rc1 < 1.0)
        Assert.IsFalse(_svc.SatisfiesConstraint("1.0~rc1", ">= 1.0"));
        // But 1.0 does satisfy >= 1.0~rc1
        Assert.IsTrue(_svc.SatisfiesConstraint("1.0", ">= 1.0~rc1"));
    }

    [TestMethod]
    public void SatisfiesConstraint_EpochAware()
    {
        // 1:1.0 > 2.0 (epoch 1 > epoch 0), so 1:1.0 satisfies >= 2.0
        Assert.IsTrue(_svc.SatisfiesConstraint("1:1.0", ">= 2.0"));
    }

    [TestMethod]
    public void SatisfiesConstraint_UbuntuVersions()
    {
        // Ubuntu-style: 1:24.004.60-1ubuntu7-anduinos > 1:24.004.60-1ubuntu6-anduinos
        Assert.IsTrue(_svc.SatisfiesConstraint("1:24.004.60-1ubuntu7-anduinos", ">= 1:24.004.60-1ubuntu6-anduinos"));
    }

    // ===================================================================
    // Ordering: dots vs digits vs letters
    // ===================================================================

    [TestMethod]
    public void Compare_DotInUpstream()
    {
        // dpskg: 1.0.0-1 > 1.0-9999 (upstream has extra ".0")
        Assert.IsTrue(_svc.Compare("1.0.0-1", "1.0-9999") > 0);
    }

    [TestMethod]
    public void Compare_UbuntuRevisionSuffix()
    {
        // dpkg: 1.0-1ubuntu1 > 1.0-1 (revision "1ubuntu1" > "1")
        // After "1", both compare: "" vs "ubuntu1" → 'u' (117) > 0, so "1ubuntu1" > "1"
        Assert.IsTrue(_svc.Compare("1.0-1ubuntu1", "1.0-1") > 0);
    }

    [TestMethod]
    public void Compare_UbuntuRevisionComplex()
    {
        // dpkg: 1.0-1ubuntu2 > 1.0-1ubuntu1
        Assert.IsTrue(_svc.Compare("1.0-1ubuntu2", "1.0-1ubuntu1") > 0);
    }

    // ===================================================================
    // Invalid version formats
    // ===================================================================

    [TestMethod]
    public void Compare_Malformed_EmptyString()
    {
        try
        {
            _svc.Compare("", "1.0");
            Assert.Fail("Expected ArgumentException for empty version string.");
        }
        catch (ArgumentException)
        {
            // Expected
        }
    }
}
