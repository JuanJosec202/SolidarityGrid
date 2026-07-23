using SolidarityGrid.Application.Abstractions;

namespace SolidarityGrid.Infrastructure.Identifiers;

public sealed class SystemIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.NewGuid();
}
