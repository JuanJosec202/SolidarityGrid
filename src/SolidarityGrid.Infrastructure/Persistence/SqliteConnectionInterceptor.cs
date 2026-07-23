using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SolidarityGrid.Infrastructure.Persistence.Configuration;

namespace SolidarityGrid.Infrastructure.Persistence;

public sealed class SqliteConnectionInterceptor(
    IOptions<PersistenceOptions> persistenceOptions) : DbConnectionInterceptor
{
    private readonly int _busyTimeoutMilliseconds =
        persistenceOptions.Value.BusyTimeoutMilliseconds;

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        ApplyConnectionPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyConnectionPragmasAsync(connection, cancellationToken);
    }

    private void ApplyConnectionPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = BuildConnectionPragmaCommand();
        command.ExecuteNonQuery();

        using var walCommand = connection.CreateCommand();
        walCommand.CommandText = "PRAGMA journal_mode = WAL;";
        walCommand.ExecuteScalar();
    }

    private async Task ApplyConnectionPragmasAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildConnectionPragmaCommand();
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var walCommand = connection.CreateCommand();
        walCommand.CommandText = "PRAGMA journal_mode = WAL;";
        await walCommand.ExecuteScalarAsync(cancellationToken);
    }

    private string BuildConnectionPragmaCommand() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {_busyTimeoutMilliseconds};");
}
