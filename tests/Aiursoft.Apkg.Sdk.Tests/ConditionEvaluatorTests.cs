using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class ConditionEvaluatorTests
{
    private readonly ConditionEvaluator _evaluator = new();

    [TestMethod]
    public void Evaluate_NullCondition_ReturnsTrue()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate(null, ctx));
    }

    [TestMethod]
    public void Evaluate_EmptyCondition_ReturnsTrue()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("", ctx));
    }

    [TestMethod]
    public void Evaluate_WhitespaceCondition_ReturnsTrue()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("   ", ctx));
    }

    [TestMethod]
    public void Evaluate_EqualityMatch_ReturnsTrue()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("'$(Suite)' == 'jammy'", ctx));
    }

    [TestMethod]
    public void Evaluate_EqualityMismatch_ReturnsFalse()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "noble", "amd64");
        Assert.IsFalse(_evaluator.Evaluate("'$(Suite)' == 'jammy'", ctx));
    }

    [TestMethod]
    public void Evaluate_InequalityMatch_ReturnsTrue()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "noble", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("'$(Suite)' != 'jammy'", ctx));
    }

    [TestMethod]
    public void Evaluate_InequalityMismatch_ReturnsFalse()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsFalse(_evaluator.Evaluate("'$(Suite)' != 'jammy'", ctx));
    }

    [TestMethod]
    public void Evaluate_PropertySubstitution_SubstitutesDistro()
    {
        var ctx = ConditionEvaluator.BuildContext("anduinos", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("'$(Distro)' == 'anduinos'", ctx));
    }

    [TestMethod]
    public void Evaluate_PropertySubstitution_SubstitutesArch()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "arm64");
        Assert.IsTrue(_evaluator.Evaluate("'$(Arch)' == 'arm64'", ctx));
    }

    [TestMethod]
    public void Evaluate_ArchitectureAlias_Works()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("'$(Architecture)' == 'amd64'", ctx));
    }

    [TestMethod]
    public void Evaluate_CaseInsensitiveComparison()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("'$(Suite)' == 'JAMMY'", ctx));
    }

    [TestMethod]
    public void Evaluate_CaseInsensitivePropertyName()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "noble", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("'$(suite)' == 'noble'", ctx));
    }

    [TestMethod]
    public void Evaluate_DoubleQuotedValues()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("\"$(Suite)\" == \"jammy\"", ctx));
    }

    [TestMethod]
    public void Evaluate_NoQuotesWorks()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.IsTrue(_evaluator.Evaluate("$(Suite) == jammy", ctx));
    }

    [TestMethod]
    public void Evaluate_MissingOperator_Throws()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        try
        {
            _evaluator.Evaluate("$(Suite) jammy", ctx);
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException) { /* expected */ }
    }

    [TestMethod]
    public void BuildContext_ContainsDistro()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.AreEqual("ubuntu", ctx["Distro"]);
    }

    [TestMethod]
    public void BuildContext_ContainsSuite()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.AreEqual("jammy", ctx["Suite"]);
    }

    [TestMethod]
    public void BuildContext_ContainsArch()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.AreEqual("amd64", ctx["Arch"]);
    }

    [TestMethod]
    public void BuildContext_ArchitectureAliasEqualsArch()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "arm64");
        Assert.AreEqual(ctx["Arch"], ctx["Architecture"]);
    }

    [TestMethod]
    public void BuildContext_ContainsComponent_WhenPassed()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64", component: "addon");
        Assert.AreEqual("addon", ctx["Component"]);
    }

    [TestMethod]
    public void BuildContext_ComponentDefaultsToEmpty()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64");
        Assert.AreEqual("", ctx["Component"]);
    }

    [TestMethod]
    public void Evaluate_ComponentInCondition()
    {
        var ctx = ConditionEvaluator.BuildContext("ubuntu", "jammy", "amd64", component: "addon");
        Assert.IsTrue(_evaluator.Evaluate("'$(Component)' == 'addon'", ctx));
    }
}
