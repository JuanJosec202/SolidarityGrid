namespace SolidarityGrid.Infrastructure.Persistence.Configuration;

public static class PersistencePath
{
    public static string Normalize(string databasePath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return databasePath;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        return Path.GetFullPath(databasePath, contentRootPath);
    }
}
