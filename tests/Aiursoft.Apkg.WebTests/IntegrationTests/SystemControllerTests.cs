using System.Net;
using Aiursoft.Apkg.Models.SystemViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class SystemControllerTests : TestBase
{
    [TestMethod]
    public async Task TestIndex()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/System/Index");
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task TestIndexContainsTableCounts()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/System/Index");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Database Table Counts"));
    }

    [TestMethod]
    public async Task TestIndexContainsMigrationInfo()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/System/Index");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Database Migrations"));
        Assert.IsTrue(html.Contains("Applied / Defined"));
    }

    [TestMethod]
    public async Task View_MigrationCard_RendersTimestamps()
    {
        // Regression: without @model, DateTime?.HasValue throws
        // RuntimeBinderException when accessed through dynamic.
        // This directly renders the view with a model containing
        // a migration that has a valid timestamp ID, verifying
        // the data-utc-time label appears.

        var engine = GetService<IRazorViewEngine>();
        var tempDataProvider = GetService<ITempDataProvider>();
        var serviceProvider = GetService<IServiceProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor
        {
            ControllerName = "System",
            ActionName = "Index"
        });

        var viewResult = engine.GetView(null, "~/Views/System/Index.cshtml", false);
        Assert.IsTrue(viewResult.Success, "View not found: ~/Views/System/Index.cshtml");
        Assert.IsNotNull(viewResult.View);

        var model = new IndexViewModel
        {
            AppliedMigrations =
            [
                new MigrationEntry { Id = "20260526053641_AddApkgUpload" }
            ]
        };

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            new TempDataDictionary(httpContext, tempDataProvider),
            writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        var html = writer.ToString();

        Assert.IsTrue(html.Contains("data-utc-time"),
            "Migration with valid timestamp ID should produce a data-utc-time label. " +
            "This means AppliedAt.HasValue was evaluated correctly.");
    }

    [TestMethod]
    public async Task TestShutdown()
    {
        await LoginAsAdmin();
        var response = await Http.PostAsync("/System/Shutdown", null);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }
}
