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
            "SolidarityGrid.Contracts",
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Node",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
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

    [Fact]
    public void DomainDoesNotUseSystemClockOrGeneratePaymentIds()
    {
        var domainPath = Path.Combine(FindRepositoryRoot(), "src", "SolidarityGrid.Domain");
        var source = ReadSourceFiles(domainPath);

        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainHasNoPackageReferences()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Domain",
            "SolidarityGrid.Domain.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("<PackageReference", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationContainsNoPersistenceImplementation()
    {
        var applicationPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Application");
        var source = ReadSourceFiles(applicationPath);
        string[] forbiddenTerms =
        [
            "DbContext",
            "EntityFramework",
            "Sqlite",
            "ConnectionString",
            "IRepository",
            "UnitOfWork",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
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

    private static string ReadSourceFiles(string directory) =>
        string.Join(
            Environment.NewLine,
            Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SolidarityGrid.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
