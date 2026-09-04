namespace cr7stream.Logic.Services;

public interface ILogoService
{
    Task<string> GetOrDownloadAsync(string? externalUrl, string slug, CancellationToken ct = default);
    string GetLocalPath(string slug);
    bool LocalFileExists(string slug);
}

