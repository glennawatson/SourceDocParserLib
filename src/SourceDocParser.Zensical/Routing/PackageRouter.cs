// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.Zensical.Routing;

/// <summary>
/// Resolves which per-package folder a given <see cref="ApiType"/>
/// belongs in. Default behaviour: the folder name is the assembly
/// name. When the caller supplies a <see cref="PackageRoutingRule"/>
/// list, the rules act both as a filter (types from non-matching
/// assemblies are excluded) and as a folder-name override (e.g. to
/// group several related assemblies under a single package folder).
/// First-match-wins.
/// </summary>
internal static class PackageRouter
{
    /// <summary>
    /// Returns the folder name for <paramref name="assemblyName"/>.
    /// When <paramref name="rules"/> is empty, the folder is the
    /// assembly name itself. When rules are present, returns the
    /// matched rule's <see cref="PackageRoutingRule.FolderName"/>;
    /// null when no rule matches (caller should skip the type).
    /// </summary>
    /// <param name="assemblyName">Assembly the type lives in (e.g. <c>Splat.Core</c>).</param>
    /// <param name="rules">Ordered routing rules from the user's options; may be empty.</param>
    /// <returns>The folder name, or null when rules are configured and none match.</returns>
    public static string? ResolveFolder(string assemblyName, PackageRoutingRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules is [])
        {
            return assemblyName;
        }

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
        if (prefix is not [_, ..])
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
