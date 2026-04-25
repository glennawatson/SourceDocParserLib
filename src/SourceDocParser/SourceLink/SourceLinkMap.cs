// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// Parsed SourceLink substitution rules for an assembly.
/// </summary>
/// <remarks>
/// Provider-agnostic: matches path prefixes to URL prefixes for remote resolution.
/// It implements the SourceLink spec where the first matching pattern in the JSON
/// declaration order wins. Matches are case-insensitive on Windows-style paths.
/// </remarks>
internal sealed class SourceLinkMap(List<SourceLinkMapEntry> entries)
{
    /// <summary>
    /// Path-prefix to URL-prefix entries in declaration order.
    /// </summary>
    private readonly List<SourceLinkMapEntry> _entries = entries;

    /// <summary>
    /// Cache of resolved local paths to remote URLs.
    /// </summary>
    private readonly Dictionary<string, string?> _resolutionCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Attempts to substitute a local file path into a remote URL.
    /// </summary>
    /// <param name="localPath">PDB-recorded path to the source file.</param>
    /// <returns>The resolved remote URL, or null if no match was found.</returns>
    public string? TryResolve(string localPath)
    {
        if (_resolutionCache.TryGetValue(localPath, out var cached))
        {
            return cached;
        }

        var resolved = ResolveCore(localPath);
        _resolutionCache[localPath] = resolved;
        return resolved;
    }

    /// <summary>
    /// Substitution logic for <see cref="TryResolve"/>.
    /// </summary>
    /// <param name="localPath">PDB-recorded path to the source file.</param>
    /// <returns>The resolved remote URL, or null.</returns>
    private string? ResolveCore(string localPath)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.IsWildcard)
            {
                if (!localPath.StartsWith(entry.LocalPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var suffix = localPath.AsSpan(entry.LocalPrefix.Length);
                var urlSuffix = suffix.ToString().Replace(Path.DirectorySeparatorChar, '/');

                if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar && Path.AltDirectorySeparatorChar != '/')
                {
                    urlSuffix = urlSuffix.Replace(Path.AltDirectorySeparatorChar, '/');
                }

                return entry.UrlPrefix + urlSuffix;
            }

            if (string.Equals(entry.LocalPrefix, localPath, StringComparison.OrdinalIgnoreCase))
            {
                return entry.UrlPrefix;
            }
        }

        return null;
    }
}
