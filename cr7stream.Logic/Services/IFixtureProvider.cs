using cr7stream.Logic.Models;

namespace cr7stream.Logic.Services;

public interface IFixtureProvider
{
    Task<FixtureData> LoadRawAsync();
    Task SaveAsync(FixtureData data, CancellationToken cancellationToken = default);
}

