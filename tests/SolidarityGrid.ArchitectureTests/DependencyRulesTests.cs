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
            "SqlConnection",
            "ConnectionString",
            "DbUpdateException",
            "SqliteErrorCode",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
    }

    [Fact]
    public void NodeContainsNoPersistenceImplementation()
    {
        var nodePath = Path.Combine(FindRepositoryRoot(), "src", "SolidarityGrid.Node");
        var source = ReadSourceFiles(nodePath);

        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRepository", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SolidarityGridUnitOfWork", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InfrastructureReferencesOnlyTheConfiguredEfCoreProvider()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SolidarityGrid.Infrastructure",
            "SolidarityGrid.Infrastructure.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore.Design", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlServer", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionCodeHasNoForbiddenPersistencePatterns()
    {
        var sourcePath = Path.Combine(FindRepositoryRoot(), "src");
        var source = ReadSourceFiles(sourcePath);
        string[] forbiddenTerms =
        [
            "GenericRepository",
            "EnsureCreated",
            "EnsureDeleted",
            "BuildServiceProvider",
            "DateTime.Now",
            "DateTimeOffset.Now",
        ];

        Assert.All(
            forbiddenTerms,
            term => Assert.DoesNotContain(term, source, StringComparison.Ordinal));
    }

    [Fact]
    public void SolutionContainsNoBrokerOrCentralDatabasePackage()
    {
        var root = FindRepositoryRoot();
        var packageFiles = Directory
            .EnumerateFiles(root, "*.props", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(root, "src"),
                "*.csproj",
                SearchOption.AllDirectories));
        var packageConfiguration = string.Join(
            Environment.NewLine,
            packageFiles.Select(File.ReadAllText));
        string[] forbiddenPackages =
        [
            "RabbitMQ",
            "Kafka",
            "MassTransit",
            "Npgsql",
            "SqlServer",
            "Redis",
        ];

        Assert.All(
            forbiddenPackages,
            package => Assert.DoesNotContain(
                package,
                packageConfiguration,
                StringComparison.OrdinalIgnoreCase));
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
