// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical;

/// <summary>
/// Resolves which per-package folder a given <see cref="ApiType"/>
/// belongs in, by matching its <see cref="ApiType.AssemblyName"/>
/// against the configured <see cref="PackageRoutingRule"/> list.
/// First-match-wins; no match returns null and the caller falls
/// back to the legacy namespace-only layout.
/// </summary>
internal static class PackageRouter
{
    /// <summary>Returns the folder name for <paramref name="assemblyName"/> per <paramref name="rules"/>; null when none match.</summary>
    /// <param name="assemblyName">Assembly the type lives in (e.g. <c>Splat.Core</c>).</param>
    /// <param name="rules">Ordered routing rules from the user's options.</param>
    /// <returns>The folder name, or null when the assembly isn't a primary package (System.*, transient deps).</returns>
    public static string? ResolveFolder(string assemblyName, PackageRoutingRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        ArgumentNullException.ThrowIfNull(rules);

        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            if (Matches(assemblyName, rule.AssemblyPrefix))
            {
                return rule.FolderName;
            }
        }

        return null;
    }

    /// <summary>Tests whether <paramref name="assemblyName"/> equals <paramref name="prefix"/> exactly or starts with <c>prefix + "."</c>.</summary>
    /// <param name="assemblyName">Candidate assembly name.</param>
    /// <param name="prefix">Bare-id prefix to match.</param>
    /// <returns>True for exact match or prefix-with-dot match.</returns>
    private static bool Matches(string assemblyName, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        if (assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return assemblyName.Length > prefix.Length
            && assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && assemblyName[prefix.Length] == '.';
    }
}
