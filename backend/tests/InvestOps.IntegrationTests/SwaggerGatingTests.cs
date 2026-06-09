using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InvestOps.IntegrationTests;

public class SwaggerGatingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SwaggerGatingTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Swagger_is_available_in_development()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();

        var swagger = await client.GetAsync("/swagger/index.html");
        var root = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
    }

    [Fact]
    public async Task Swagger_is_available_in_staging()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseEnvironment("Staging")).CreateClient();

        var swagger = await client.GetAsync("/swagger/index.html");
        var root = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
    }

    [Fact]
    public async Task Swagger_is_disabled_in_production()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production")).CreateClient();

        var swagger = await client.GetAsync("/swagger/index.html");
        var root = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
    }
}
