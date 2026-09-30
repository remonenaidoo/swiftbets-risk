using System.Reflection;
using NetArchTest.Rules;

namespace SwiftBets.Risk.ArchitectureTests;

public sealed class LayerTests
{
    private static readonly Assembly Domain = typeof(SwiftBets.Risk.Domain.DomainAssembly).Assembly;
    private static readonly Assembly Application = typeof(SwiftBets.Risk.Application.ApplicationRegistration).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else_in_the_solution() =>
        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny("SwiftBets.Risk.Application", "SwiftBets.Risk.Infrastructure", "SwiftBets.BuildingBlocks", "Microsoft.AspNetCore", "Dapper")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Application_does_not_depend_on_infrastructure() =>
        Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny("SwiftBets.Risk.Infrastructure", "Dapper", "Microsoft.Data.SqlClient", "Confluent.Kafka", "StackExchange.Redis", "Npgsql")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Domain_assembly_references_no_other_project() =>
        Domain.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain(n => n!.StartsWith("SwiftBets.", StringComparison.Ordinal));
}
