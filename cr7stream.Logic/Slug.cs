using System.Text.RegularExpressions;

namespace cr7stream.Logic;

public static class Slug
{
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-");
        return slug.Trim('-');
    }
}

