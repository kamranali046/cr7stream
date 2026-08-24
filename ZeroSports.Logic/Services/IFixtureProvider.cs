using ZeroSports.Logic.Models;

namespace ZeroSports.Logic.Services;

public interface IFixtureProvider
{
    Task<FixtureData> LoadRawAsync();
    Task SaveAsync(FixtureData data, CancellationToken cancellationToken = default);
}
