namespace Autotrade.Llm;

internal static class DotEnvReader
{
    public static string? TryGetValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = trimmed[..separator].Trim();
                if (!string.Equals(name, key, StringComparison.Ordinal))
                {
                    continue;
                }

                return Unquote(trimmed[(separator + 1)..].Trim());
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return Path.Combine(current, ".env");
            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
