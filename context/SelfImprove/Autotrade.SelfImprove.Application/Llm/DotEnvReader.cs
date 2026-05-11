namespace Autotrade.SelfImprove.Application.Llm;

internal static class DotEnvReader
{
    public static string? TryGetValue(string key)
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, ".env");
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

                var separator = trimmed.IndexOf('=', StringComparison.Ordinal);
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

    private static IEnumerable<string> CandidateRoots()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
