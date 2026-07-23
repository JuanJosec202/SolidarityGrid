using Microsoft.Extensions.Options;
using SolidarityGrid.Infrastructure.Persistence.Configuration;
using Xunit;

namespace SolidarityGrid.UnitTests.Persistence;

public sealed class PersistenceOptionsTests
{
    private readonly PersistenceOptionsValidator _validator = new();

    [Fact]
    public void ValidRelativeDatabasePathPassesValidation()
    {
        var result = Validate("data/node.db", 5000);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidAbsoluteDatabasePathPassesValidation()
    {
        var result = Validate(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "node.db")),
            5000);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyDatabasePathFailsValidation(string databasePath)
    {
        var result = Validate(databasePath, 5000);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("DatabasePath is required", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectoryOnlyPathFailsValidation()
    {
        var result = Validate($"data{Path.DirectorySeparatorChar}", 5000);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("file name", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidExtensionFailsValidation()
    {
        var result = Validate("data/node.sqlite", 5000);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(".db extension", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveBusyTimeoutFailsValidation(int busyTimeoutMilliseconds)
    {
        var result = Validate("data/node.db", busyTimeoutMilliseconds);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("greater than zero", StringComparison.Ordinal));
    }

    [Fact]
    public void RelativePathIsNormalizedAgainstContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "SolidarityGrid.Options");

        var result = PersistencePath.Normalize("data/node.db", contentRoot);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, "data", "node.db")),
            result);
    }

    [Fact]
    public void AbsolutePathRemainsAbsolute()
    {
        var absolutePath = Path.Combine(
            Path.GetTempPath(),
            "SolidarityGrid.Options",
            "node.db");

        var result = PersistencePath.Normalize(absolutePath, AppContext.BaseDirectory);

        Assert.Equal(Path.GetFullPath(absolutePath), result);
    }

    private ValidateOptionsResult Validate(
        string databasePath,
        int busyTimeoutMilliseconds) =>
        _validator.Validate(
            Options.DefaultName,
            new PersistenceOptions
            {
                DatabasePath = databasePath,
                BusyTimeoutMilliseconds = busyTimeoutMilliseconds,
            });
}
