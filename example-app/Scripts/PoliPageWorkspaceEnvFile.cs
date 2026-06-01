namespace PoliPage.AspNetCore.ExampleApp.Scripts;

// Loads /Users/mickael/Projects/.env (or the nearest .env walking up from the working
// directory) into IConfiguration. Real env vars always win — only keys absent from the
// process environment are pushed in from the file. Mirrors the symfony-bundle / nestjs
// example apps: single root .env, no per-app .env.local. See CLAUDE.md §10.5.
internal static class PoliPageWorkspaceEnvFile
{
    // POLI_PAGE_* → PoliPage:* in IConfiguration. The .env files at the workspace root use
    // the flat shell-friendly name; the .NET integration's options bind from the hierarchical
    // path. Limited to the SDK + integration options surface so unrelated env entries don't
    // accidentally cross-pollute the config tree.
    private static readonly Dictionary<string, string> KeyMapping = new(StringComparer.Ordinal)
    {
        ["POLI_PAGE_API_KEY"] = "PoliPage:ApiKey",
        ["POLI_PAGE_BASE_URL"] = "PoliPage:BaseUrl",
        ["POLI_PAGE_REQUEST_TIMEOUT"] = "PoliPage:RequestTimeout",
        ["POLI_PAGE_MAX_RETRIES"] = "PoliPage:MaxRetries",
    };

    public static IConfigurationBuilder AddPoliPageWorkspaceEnvFile(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var pairs = new Dictionary<string, string?>(StringComparer.Ordinal);

        var envFile = LocateEnvFile(Directory.GetCurrentDirectory());
        if (envFile is not null)
        {
            foreach (var raw in File.ReadAllLines(envFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                    continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                // Strip a matching pair of surrounding quotes.
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }

                // Real shell exports always win — handled by the env-var pass below.
                var configKey = KeyMapping.TryGetValue(key, out var mapped) ? mapped : key;
                pairs[configKey] = value;
            }
        }

        // Env vars override .env. The default AddEnvironmentVariables() that CreateBuilder
        // wires only knows the double-underscore hierarchical convention (POLI_PAGE__API_KEY),
        // not the flat shell-style names the sister integrations standardised on
        // (POLI_PAGE_API_KEY). This loop translates the known POLI_PAGE_* env vars into the
        // hierarchical PoliPage:* config keys the SDK + integration options bind from.
        foreach (var (envName, configKey) in KeyMapping)
        {
            var envValue = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrEmpty(envValue))
                pairs[configKey] = envValue;
        }

        return pairs.Count == 0 ? builder : builder.AddInMemoryCollection(pairs);
    }

    private static string? LocateEnvFile(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
