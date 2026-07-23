namespace SolidarityGrid.Infrastructure.Persistence.Initialization;

public sealed class DatabaseInitializationState
{
    private bool _isInitialized;

    public bool IsInitialized => Volatile.Read(ref _isInitialized);

    public void MarkInitialized()
    {
        Volatile.Write(ref _isInitialized, true);
    }
}
