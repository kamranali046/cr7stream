using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using cr7stream.Logic.Models;

namespace cr7stream.Logic.Services;

public class JsonFixtureProvider : IFixtureProvider
{
    private readonly IWebHostEnvironment _environment;
    private readonly IMemoryCache _memoryCache;
    private const string RelativePath = "data/fixtures.json";
    private const string CacheKey = "FixturesData";

    private static readonly JsonSerializerOptions s_readOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    // Serializes all reads/writes to prevent read-modify-write races.
    private static readonly SemaphoreSlim s_fileLock = new(1, 1);

    // Short-lived cache to avoid re-deserializing on every request.
    private static FixtureData? s_cache;
    private static DateTime s_cacheUtc = DateTime.MinValue;
    private static readonly TimeSpan s_cacheTtl = TimeSpan.FromSeconds(30);

    // Memory cache for longer-lived fixture data (bypasses file read on cache hit).
    private static readonly MemoryCacheEntryOptions s_memoryCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30),
        Size = 1
    };

    public JsonFixtureProvider(IWebHostEnvironment environment, IMemoryCache memoryCache)
    {
        _environment = environment;
        _memoryCache = memoryCache;
    }

    public async Task<FixtureData> LoadRawAsync()
    {
        // Check memory cache first.
        if (_memoryCache.TryGetValue(CacheKey, out FixtureData? cached) && cached is not null)
        {
            return cached;
        }

        // Check static cache if fresh enough.
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
                _memoryCache.Set(CacheKey, s_cache, s_memoryCacheOptions);
                return s_cache;
            }

            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<FixtureData>(stream, s_readOptions);
            s_cache = data ?? new FixtureData();
            s_cacheUtc = DateTime.UtcNow;
            _memoryCache.Set(CacheKey, s_cache, s_memoryCacheOptions);
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
            _memoryCache.Set(CacheKey, data, s_memoryCacheOptions);
        }
        finally
        {
            s_fileLock.Release();
        }
    }
}

