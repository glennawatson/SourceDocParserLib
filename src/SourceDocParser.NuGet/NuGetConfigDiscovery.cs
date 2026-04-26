// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet;

/// <summary>
/// Walks the standard NuGet configuration discovery chain from a
/// caller-supplied working folder — same precedence order the SDK
/// uses (walk-from-cwd-up first, then user-scoped). Each helper
/// composes <see cref="NuGetGlobalCache.GetUserNuGetConfigPaths"/>
/// + <see cref="NuGetConfigReader.ReadGlobalPackagesFolderAsync(string, System.Threading.CancellationToken)"/>
/// so callers don't have to re-implement the path-walk shape.
/// </summary>
internal static class NuGetConfigDiscovery
{
    /// <summary>The fallback feed used when no config along the chain declares any package sources.</summary>
    public static readonly PackageSource DefaultNuGetOrgSource = new("nuget.org", "https://api.nuget.org/v3/index.json");

    /// <summary>Both filename casings NuGet honours on case-sensitive filesystems.</summary>
    private static readonly string[] _configFileNames = ["nuget.config", "NuGet.Config"];

    /// <summary>
    /// Yields every <c>nuget.config</c> path NuGet would consult,
    /// in precedence order — caller's <paramref name="workingFolder"/>,
    /// each ancestor up to the filesystem root, then the user-scoped
    /// locations. First-existing-file wins for any given setting.
    /// Doesn't probe disk beyond <see cref="File.Exists(string)"/>;
    /// callers can fold the existence check into the read step.
    /// </summary>
    /// <param name="workingFolder">Repository / project root to start the walk from.</param>
    /// <returns>Candidate config paths in NuGet precedence order.</returns>
    public static IEnumerable<string> EnumerateConfigPaths(string workingFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolder);

        var dir = Path.GetFullPath(workingFolder);
        while (!string.IsNullOrEmpty(dir))
        {
            for (var i = 0; i < _configFileNames.Length; i++)
            {
                var candidate = Path.Combine(dir, _configFileNames[i]);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                    break;
                }
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || string.Equals(parent, dir, StringComparison.Ordinal))
            {
                break;
            }

            dir = parent;
        }

        var userScoped = NuGetGlobalCache.GetUserNuGetConfigPaths();
        for (var i = 0; i < userScoped.Length; i++)
        {
            if (File.Exists(userScoped[i]))
            {
                yield return userScoped[i];
            }
        }
    }

    /// <summary>
    /// Walks the discovery chain rooted at <paramref name="workingFolder"/>
    /// and returns the first <c>globalPackagesFolder</c> value found.
    /// Returns <see langword="null"/> when no config file along the
    /// chain carries that setting — caller falls back to the
    /// platform default via
    /// <see cref="NuGetGlobalCache.ResolveGlobalPackagesFolder(string?)"/>.
    /// </summary>
    /// <param name="workingFolder">Repository / project root to start the walk from.</param>
    /// <param name="cancellationToken">Token observed across each parse.</param>
    /// <returns>The configured value, or <see langword="null"/> when absent.</returns>
    public static async Task<string?> ResolveGlobalPackagesFolderAsync(string workingFolder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolder);

        // Walk most-specific first (working folder + ancestors, then
        // user-scoped). Matches NuGet's reverse-merge effective order:
        // - Found in a closer file → that value wins
        // - <clear/> in a closer file → wipe parents, fall through to
        //   the platform default (return null)
        // - NotMentioned in a closer file → keep walking
        foreach (var configPath in EnumerateConfigPaths(workingFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(configPath, cancellationToken).ConfigureAwait(false);
            switch (result.State)
            {
                case SettingState.Found:
                    return result.Value;

                case SettingState.Cleared:
                    return null;

                default:
                    continue;
            }
        }

        return null;
    }

    /// <summary>
    /// One-shot wrapper that returns the fully-resolved global
    /// packages folder for <paramref name="workingFolder"/> —
    /// chains <see cref="ResolveGlobalPackagesFolderAsync"/> +
    /// <see cref="NuGetGlobalCache.ResolveGlobalPackagesFolder(string?)"/>
    /// so callers get the same value the SDK would land on without
    /// composing the two layers themselves.
    /// </summary>
    /// <param name="workingFolder">Repository / project root to start the walk from.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>The absolute path to the global packages folder.</returns>
    public static async Task<string> ResolveAsync(string workingFolder, CancellationToken cancellationToken = default)
    {
        var configValue = await ResolveGlobalPackagesFolderAsync(workingFolder, cancellationToken).ConfigureAwait(false);
        return NuGetGlobalCache.ResolveGlobalPackagesFolder(configValue);
    }

    /// <summary>
    /// Walks the discovery chain rooted at
    /// <paramref name="workingFolder"/> and returns the merged
    /// list of <c>&lt;packageSources&gt;</c> entries — closer
    /// files' <c>&lt;clear/&gt;</c> wipes parents, closer files'
    /// <c>&lt;add&gt;</c> entries take precedence over the same
    /// key in less-specific files. When no config along the
    /// chain declares any sources, returns the well-known
    /// nuget.org default so the fetcher always has at least one
    /// place to look.
    /// </summary>
    /// <param name="workingFolder">Repository / project root to start the walk from.</param>
    /// <param name="cancellationToken">Token observed across each parse.</param>
    /// <returns>Ordered, deduplicated package sources — first entry has highest discovery precedence.</returns>
    public static async Task<PackageSource[]> ResolvePackageSourcesAsync(string workingFolder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolder);

        // Walk most-specific first so closer files' adds upsert
        // first (their value wins via the seenKeys gate). Stop
        // walking when a closer file invoked <clear/> — its clear
        // erases anything the less-specific configs would have
        // contributed.
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<PackageSource>();

        foreach (var configPath in EnumerateConfigPaths(workingFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileResult = await PackageSourcesReader.ReadPackageSourcesAsync(configPath, cancellationToken).ConfigureAwait(false);

            for (var i = 0; i < fileResult.Sources.Length; i++)
            {
                var source = fileResult.Sources[i];
                if (seenKeys.Add(source.Key))
                {
                    merged.Add(source);
                }
            }

            if (fileResult.ClearedSeen)
            {
                return [.. merged];
            }
        }

        if (merged.Count == 0)
        {
            return [DefaultNuGetOrgSource];
        }

        return [.. merged];
    }
}
