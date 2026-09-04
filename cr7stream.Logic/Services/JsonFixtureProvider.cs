using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using cr7stream.Logic.Models;

namespace cr7stream.Logic.Services;

public class JsonFixtureProvider : IFixtureProvider
{
    private readonly IWebHostEnvironment _environment;
    private const string RelativePath = "data/fixtures.json";

    private static readonly JsonSerializerOptions s_readOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    // Serializes all reads/writes to prevent read-modify-write races.
    private static readonly SemaphoreSlim s_fileLock = new(1, 1);

    // Short-lived cache to avoid re-deserializing on every request.
    private static FixtureData? s_cache;
    private static DateTime s_cacheUtc = DateTime.MinValue;
    private static readonly TimeSpan s_cacheTtl = TimeSpan.FromSeconds(30);

    public JsonFixtureProvider(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<FixtureData> LoadRawAsync()
    {
        // Return cached data if fresh enough.
        if (s_cache is not null && (DateTime.UtcNow - s_cacheUtc) < s_cacheTtl)
        {
            return s_cache;
        }

        await s_fileLock.WaitAsync();
        try
        {
            // Double-check after acquiring the lock.
            if (s_cache is not null && (DateTime.UtcNow - s_cacheUtc) < s_cacheTtl)
            {
                return s_cache;
            }

            var path = Path.Combine(_environment.WebRootPath, RelativePath);

            if (!File.Exists(path))
            {
                s_cache = new FixtureData();
                s_cacheUtc = DateTime.UtcNow;
                return s_cache;
            }

            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<FixtureData>(stream, s_readOptions);
            s_cache = data ?? new FixtureData();
            s_cacheUtc = DateTime.UtcNow;
            return s_cache;
        }
        finally
        {
            s_fileLock.Release();
        }
    }

    public async Task SaveAsync(FixtureData data, CancellationToken cancellationToken = default)
    {
        await s_fileLock.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(_environment.WebRootPath, RelativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Atomic write: serialize to temp file, then replace.
            var tempPath = path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, data, s_writeOptions, CancellationToken.None);
            }

            File.Move(tempPath, path, overwrite: true);

            // Update cache.
            s_cache = data;
            s_cacheUtc = DateTime.UtcNow;
        }
        finally
        {
            s_fileLock.Release();
        }
    }
}

