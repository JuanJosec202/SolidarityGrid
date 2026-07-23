using Microsoft.EntityFrameworkCore;
using SolidarityGrid.Domain.Payments;

namespace SolidarityGrid.Infrastructure.Persistence;

public sealed class SolidarityGridDbContext(
    DbContextOptions<SolidarityGridDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SolidarityGridDbContext).Assembly);
    }
}
