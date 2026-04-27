// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Builds the canonical <em>package → namespace → entries</em> tree
/// shape used by the landing page generator and the navigation
/// emitter. Both consumers had near-identical bucketing loops; the
/// generic helper folds them into one place. Entry materialisation
/// stays caller-side via <paramref name="entryFactory"/> so leaf
/// records can be file-private to each consumer.
/// </summary>
internal static class PackageNamespaceTreeBuilder
{
    /// <summary>The namespace key surfaced for types whose namespace is empty.</summary>
    private const string GlobalNamespaceKey = "(global)";

    /// <summary>
    /// Buckets <paramref name="types"/> into a sorted
    /// package → namespace → list-of-entry tree. Types whose assembly
    /// has no matching <see cref="PackageRoutingRule"/> are skipped —
    /// they wouldn't have type pages either.
    /// </summary>
    /// <typeparam name="TEntry">The leaf entry record type produced by <paramref name="entryFactory"/>.</typeparam>
    /// <param name="types">All types that received pages.</param>
    /// <param name="packageRouting">Routing rules used to resolve each type's package folder.</param>
    /// <param name="entryFactory">Function producing one leaf entry per type.</param>
    /// <param name="entryComparer">Comparator used to sort each namespace's entries by display order.</param>
    /// <returns>The ordered tree.</returns>
    public static SortedDictionary<string, SortedDictionary<string, List<TEntry>>> Build<TEntry>(
        ApiType[] types,
        PackageRoutingRule[] packageRouting,
        Func<ApiType, TEntry> entryFactory,
        Comparison<TEntry> entryComparer)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(packageRouting);
        ArgumentNullException.ThrowIfNull(entryFactory);
        ArgumentNullException.ThrowIfNull(entryComparer);

        var tree = new SortedDictionary<string, SortedDictionary<string, List<TEntry>>>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var package = PackageRouter.ResolveFolder(type.AssemblyName, packageRouting);
            if (package is null)
            {
                continue;
            }

            var ns = type.Namespace is [_, ..] ? type.Namespace : GlobalNamespaceKey;
            var entries = GetOrCreateBucket(tree, package, ns);
            entries.Add(entryFactory(type));
        }

        foreach (var nsBucket in tree.Values)
        {
            foreach (var entries in nsBucket.Values)
            {
                entries.Sort(entryComparer);
            }
        }

        return tree;
    }

    /// <summary>Gets or creates the (package, namespace) bucket inside <paramref name="tree"/>.</summary>
    /// <typeparam name="TEntry">Leaf entry type.</typeparam>
    /// <param name="tree">Outer tree.</param>
    /// <param name="package">Package folder name.</param>
    /// <param name="ns">Namespace key.</param>
    /// <returns>The list to which the new entry should be appended.</returns>
    private static List<TEntry> GetOrCreateBucket<TEntry>(
        SortedDictionary<string, SortedDictionary<string, List<TEntry>>> tree,
        string package,
        string ns)
    {
        if (!tree.TryGetValue(package, out var nsBucket))
        {
            nsBucket = new(StringComparer.Ordinal);
            tree[package] = nsBucket;
        }

        if (!nsBucket.TryGetValue(ns, out var entries))
        {
            entries = [];
            nsBucket[ns] = entries;
        }

        return entries;
    }
}
