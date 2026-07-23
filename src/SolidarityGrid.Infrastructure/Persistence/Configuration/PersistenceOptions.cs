namespace SolidarityGrid.Infrastructure.Persistence.Configuration;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public string DatabasePath { get; set; } = string.Empty;

    public int BusyTimeoutMilliseconds { get; set; } = 5000;
}
