// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Discovers <c>ref/</c> directories inside locally-installed .NET
/// SDK ref packs (e.g.
/// <c>~/.dotnet/packs/Microsoft.WindowsDesktop.App.Ref/8.0.10/ref/net8.0</c>)
/// that match a target TFM, so the resolver can fall back to those
/// DLLs when a transitive reference belongs to a SDK / workload that
/// doesn't ship as a NuGet package.
///
/// Layout assumed:
/// <c>&lt;packsRoot&gt;/&lt;PackName&gt;/&lt;Version&gt;/ref/&lt;tfm&gt;/*.dll</c>.
/// Only packs whose name ends in <c>.Ref</c> are considered (the dotnet
/// SDK reserves that suffix for ref packs); workload runtime packs
/// without ref dirs are skipped silently. Highest-version dir per
/// pack name wins so multi-installed SDKs don't yield duplicate refs
/// with stale shapes.
/// </summary>
internal static class RefPackProbe
{
    /// <summary>The conventional <c>.Ref</c> suffix used by every dotnet SDK ref pack.</summary>
    private const string RefPackSuffix = ".Ref";

    /// <summary>The <c>ref/</c> subfolder under <c>&lt;pack&gt;/&lt;version&gt;</c>.</summary>
    private const string RefSubfolder = "ref";

    /// <summary>
    /// Culture-stable, numerically-aware string comparer (so
    /// <c>9.0.0-preview.10</c> sorts after <c>9.0.0-preview.2</c>).
    /// Used only on the rare prerelease-only fallback path.
    /// </summary>
    private static readonly StringComparer NumericOrderingComparer =
        StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, System.Globalization.CompareOptions.NumericOrdering);

    /// <summary>
    /// Returns the <c>ref/&lt;tfm&gt;</c> directories from every ref
    /// pack under <paramref name="packRoots"/> whose layout matches
    /// any of <paramref name="compatibleTfms"/>. Result is ordered by
    /// pack-root priority first, then by TFM rank within each pack.
    /// </summary>
    /// <param name="packRoots">Pack-root directories from <see cref="DotNetSdkLocator.EnumeratePackRoots()"/>.</param>
    /// <param name="compatibleTfms">TFMs the consumer is willing to accept (target TFM first, then lower-rank fallbacks).</param>
    /// <returns>The matching <c>ref/&lt;tfm&gt;</c> directories in scan order.</returns>
    public static List<string> ProbeRefPackRefDirs(
        IReadOnlyList<string> packRoots,
        IReadOnlyList<string> compatibleTfms)
    {
        ArgumentNullException.ThrowIfNull(packRoots);
        ArgumentNullException.ThrowIfNull(compatibleTfms);

        if (packRoots is [] || compatibleTfms is [])
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs = new List<string>();
        for (var i = 0; i < packRoots.Count; i++)
        {
            AppendRefDirsForPackRoot(packRoots[i], compatibleTfms, seen, dirs);
        }

        return dirs;
    }

    /// <summary>
    /// Walks every <c>*.Ref/&lt;version&gt;/ref/&lt;tfm&gt;</c> dir
    /// under one pack root and appends the matches to
    /// <paramref name="dirs"/>.
    /// </summary>
    /// <param name="packRoot">Single packs/ root.</param>
    /// <param name="compatibleTfms">TFMs the consumer accepts.</param>
    /// <param name="seen">Dedupe set, mutated in place.</param>
    /// <param name="dirs">Output list, mutated in place.</param>
    private static void AppendRefDirsForPackRoot(
        string packRoot,
        IReadOnlyList<string> compatibleTfms,
        HashSet<string> seen,
        List<string> dirs)
    {
        if (!Directory.Exists(packRoot))
        {
            return;
        }

        var packDirs = SafeGetDirectories(packRoot);
        for (var i = 0; i < packDirs.Length; i++)
        {
            AppendRefDirsForPackDir(packDirs[i], compatibleTfms, seen, dirs);
        }
    }

    /// <summary>
    /// Visits one <c>&lt;packsRoot&gt;/&lt;PackName&gt;</c> directory
    /// and emits the matching <c>ref/&lt;tfm&gt;</c> dirs after the
    /// pack-name suffix and version-dir filters apply.
    /// </summary>
    /// <param name="packDir">A single pack directory candidate.</param>
    /// <param name="compatibleTfms">TFMs the consumer accepts.</param>
    /// <param name="seen">Dedupe set, mutated in place.</param>
    /// <param name="dirs">Output list, mutated in place.</param>
    private static void AppendRefDirsForPackDir(
        string packDir,
        IReadOnlyList<string> compatibleTfms,
        HashSet<string> seen,
        List<string> dirs)
    {
        var packName = Path.GetFileName(packDir);
        if (!packName.EndsWith(RefPackSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var bestVersion = FindHighestVersionedSubdir(packDir);
        if (bestVersion is null)
        {
            return;
        }

        var refDir = Path.Combine(bestVersion, RefSubfolder);
        if (!Directory.Exists(refDir))
        {
            return;
        }

        for (var t = 0; t < compatibleTfms.Count; t++)
        {
            var candidate = Path.Combine(refDir, compatibleTfms[t]);
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (seen.Add(canonical))
            {
                dirs.Add(canonical);
            }
        }
    }

    /// <summary>
    /// Wraps <see cref="Directory.GetDirectories(string)"/> with
    /// permission/IO error suppression so a single unreadable pack
    /// folder doesn't break discovery.
    /// </summary>
    /// <param name="dir">The directory to enumerate.</param>
    /// <returns>The subdirectory paths, or an empty array on error.</returns>
    private static string[] SafeGetDirectories(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// Returns the highest-numbered version subdirectory under
    /// <paramref name="packDir"/>. Prefers <see cref="Version"/>-
    /// parseable dirs (the stable case -- <c>8.0.10</c>, etc.); when
    /// none parse, falls back to ordinal-numeric string comparison
    /// so prerelease-only installs (<c>9.0.0-preview.x</c>) still
    /// pick the latest entry.
    /// </summary>
    /// <param name="packDir">The pack's root (e.g. <c>.../Microsoft.WindowsDesktop.App.Ref</c>).</param>
    /// <returns>The absolute path of the winning version dir, or null when the dir is empty or unreadable.</returns>
    private static string? FindHighestVersionedSubdir(string packDir)
    {
        var versionDirs = SafeGetDirectories(packDir);
        if (versionDirs.Length is 0)
        {
            return null;
        }

        string? bestPath = null;
        Version? bestVersion = null;
        for (var i = 0; i < versionDirs.Length; i++)
        {
            var candidate = versionDirs[i];
            if (!Version.TryParse(Path.GetFileName(candidate), out var version))
            {
                continue;
            }

            if (bestVersion is null || version.CompareTo(bestVersion) > 0)
            {
                bestPath = candidate;
                bestVersion = version;
            }
        }

        if (bestPath is not null)
        {
            return bestPath;
        }

        // Prerelease-only fallback: stable lexicographic-numeric sort.
        Array.Sort(versionDirs, static (a, b) =>
            NumericOrderingComparer.Compare(Path.GetFileName(b), Path.GetFileName(a)));
        return versionDirs[0];
    }
}
