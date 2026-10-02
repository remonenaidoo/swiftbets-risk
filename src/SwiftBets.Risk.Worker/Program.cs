using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Infrastructure;
using SwiftBets.Risk.Worker.Endpoints;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-risk");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddRiskApplication();
builder.Services.AddRiskInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapRiskEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "swiftbets-risk" })).ExcludeFromDescription();

await app.RunAsync();
return 0;

public partial class Program;
