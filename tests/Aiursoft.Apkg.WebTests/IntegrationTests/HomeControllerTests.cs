namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class HomeControllerTests : TestBase
{
    [TestMethod]
    public async Task GetIndex()
    {
        var url = "/";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task GetSelfHost()
    {
        var response = await Http.GetAsync("/Home/SelfHost");
        response.EnsureSuccessStatusCode();
    }
}
