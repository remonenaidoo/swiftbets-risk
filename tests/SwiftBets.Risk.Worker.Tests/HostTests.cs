using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SwiftBets.Risk.Worker.Tests;

public sealed class HostTests : IClassFixture<HostTests.Factory>
{
    private readonly HttpClient _client;

    public HostTests(Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Liveness_is_healthy_without_dependencies()
    {
        using var response = await _client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_reports_unavailable_dependencies()
    {
        using var response = await _client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Unknown_route_returns_the_error_envelope()
    {
        using var response = await _client.GetAsync(new Uri("/no-such-route", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Metrics_are_exposed()
    {
        var body = await _client.GetStringAsync(new Uri("/metrics", UriKind.Relative), TestContext.Current.CancellationToken);

        body.ShouldContain("process_cpu_seconds_total");
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Kafka:BootstrapServers", "127.0.0.1:1");
            builder.UseSetting("ConnectionStrings:SbRisk", "Host=127.0.0.1;Port=1;Database=sb_risk;Username=none;Password=none");
            builder.UseSetting("Risk:RunConsumers", "false");
            builder.UseSetting("Jwt:Authority", "http://127.0.0.1:1");
            builder.UseSetting("Jwt:RequireHttpsMetadata", "false");
        }
    }
}
