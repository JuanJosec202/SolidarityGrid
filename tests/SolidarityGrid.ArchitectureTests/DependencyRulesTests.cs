using System.Reflection;
using SolidarityGrid.Application;
using SolidarityGrid.Contracts;
using SolidarityGrid.Domain;
using SolidarityGrid.Infrastructure;
using Xunit;

namespace SolidarityGrid.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void DomainHasNoForbiddenReferences()
    {
        AssertHasNoReferences(
            typeof(DomainAnchor).Assembly,
            "SolidarityGrid.Application",
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Node",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Grpc");
    }

    [Fact]
    public void ApplicationHasNoForbiddenReferences()
    {
        AssertHasNoReferences(
            typeof(ApplicationAnchor).Assembly,
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Node",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Grpc");
    }

    [Fact]
    public void ContractsHasNoForbiddenReferences()
    {
        AssertHasNoReferences(
            typeof(ContractsAnchor).Assembly,
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Node");
    }

    [Fact]
    public void InfrastructureHasNoNodeReference()
    {
        AssertHasNoReferences(typeof(InfrastructureAnchor).Assembly, "SolidarityGrid.Node");
    }

    private static void AssertHasNoReferences(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            references,
            reference => forbiddenPrefixes.Any(
                prefix => reference.Equals(prefix, StringComparison.Ordinal) ||
                          reference.StartsWith($"{prefix}.", StringComparison.Ordinal)));
    }
}
