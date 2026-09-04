using cr7stream.Logic.Models;

namespace cr7stream.Logic;

/// <summary>
/// Shared player merge logic used by both ScrapperLogic and AdminLogic.
/// Preserves custom players, matches existing by URL, and appends new ones.
/// </summary>
internal static class PlayerMergeHelper
{
    internal static List<Player> MergePlayers(List<Player>? existing, List<Player> fetched)
    {
        existing ??= new List<Player>();
        var result = new List<Player>();
        var resultUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var player in existing)
        {
            if (player.IsCustom)
            {
                result.Add(player);
                resultUrls.Add(player.Url);
                continue;
            }

            var match = fetched.FirstOrDefault(f => string.Equals(f.Url, player.Url, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.Enabled = player.Enabled;
                result.Add(match);
                resultUrls.Add(match.Url);
            }
        }

        foreach (var player in fetched)
        {
            if (resultUrls.Add(player.Url))
            {
                result.Add(player);
            }
        }

        return result;
    }
}

