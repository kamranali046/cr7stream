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

    // Static cache references for explicitly clearing state
    private static FixtureData? s_cache;
    private static DateTime s_cacheUtc = DateTime.MinValue;

    public JsonFixtureProvider(IWebHostEnvironment environment, IMemoryCache memoryCache)
    {
        _environment = environment;
        _memoryCache = memoryCache;
    }

    public async Task<FixtureData> LoadRawAsync()
    {
        await s_fileLock.WaitAsync();
        try
        {
            var path = Path.Combine(_environment.ContentRootPath, "wwwroot", RelativePath);

            if (!File.Exists(path))
            {
                return new FixtureData();
            }

            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<FixtureData>(stream, s_readOptions);
            return data ?? new FixtureData();
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
            var path = Path.Combine(_environment.ContentRootPath, "wwwroot", RelativePath);
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

            // Invalidate all in-memory references immediately
            s_cache = null;
            s_cacheUtc = DateTime.MinValue;
            _memoryCache.Remove(CacheKey);
        }
        finally
        {
            s_fileLock.Release();
        }
    }
}