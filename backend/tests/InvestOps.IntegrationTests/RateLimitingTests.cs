using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InvestOps.IntegrationTests;

public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient CreateClientWithLimits(int permitLimit, int writePermitLimit) =>
        _factory.WithWebHostBuilder(b => b
            .UseSetting("RateLimiting:PermitLimit", permitLimit.ToString())
            .UseSetting("RateLimiting:WritePermitLimit", writePermitLimit.ToString())
            .UseSetting("RateLimiting:WindowSeconds", "60")).CreateClient();

    [Fact]
    public async Task Read_endpoints_return_429_with_retry_after_when_limit_exceeded()
    {
        var client = CreateClientWithLimits(permitLimit: 3, writePermitLimit: 2);

        for (var i = 0; i < 3; i++)
        {
            var allowed = await client.GetAsync("/api/products");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var rejected = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Write_endpoints_use_stricter_limit_than_reads()
    {
        var client = CreateClientWithLimits(permitLimit: 10, writePermitLimit: 2);

        for (var i = 0; i < 2; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/operations/import", new { });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var rejected = await client.PostAsJsonAsync("/api/operations/import", new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_are_exempt_from_rate_limiting()
    {
        var client = CreateClientWithLimits(permitLimit: 2, writePermitLimit: 2);

        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
