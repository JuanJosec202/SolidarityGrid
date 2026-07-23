namespace SolidarityGrid.Infrastructure.Persistence.Initialization;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
