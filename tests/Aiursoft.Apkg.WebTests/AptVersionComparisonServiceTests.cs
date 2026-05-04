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
}
