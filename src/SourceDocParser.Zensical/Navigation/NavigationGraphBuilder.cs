// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// Builds a typed, immutable <see cref="NavigationGraph"/> describing
/// the API pages <see cref="ZensicalDocumentationEmitter"/> writes.
/// Tree shape is package -&gt; namespace -&gt; type, ordinally sorted
/// at every level. Page paths use forward slashes regardless of host
/// OS so the graph is portable across build agents.
///
/// Serialisation is deliberately out of scope: Zensical's
/// <see href="https://zensical.org/docs/setup/navigation/#navigation-integration-with-navigation-integration">explicit
/// navigation</see> can be authored as TOML, YAML, or JSON, and
/// consumers usually splice the API tree into a hand-written nav
/// graph (their own landing pages, guides, etc.). They walk the
/// returned <see cref="NavigationGraph"/> with indexed
/// <c>for</c> loops and emit whatever format their pipeline needs.
/// </summary>
public sealed class NavigationGraphBuilder
{
    /// <summary>The namespace key surfaced for types whose namespace is empty.</summary>
    private const string GlobalNamespaceKey = "(global)";

    /// <summary>Routing options -- drive the package-folder grouping.</summary>
    private readonly ZensicalEmitterOptions _options;

    /// <summary>Initializes a new instance of the <see cref="NavigationGraphBuilder"/> class.</summary>
    /// <param name="options">Routing + cross-link tunables (only the routing rules are used here).</param>
    public NavigationGraphBuilder(ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Builds the navigation graph for <paramref name="types"/>. Types
    /// whose assembly has no matching <see cref="PackageRoutingRule"/>
    /// are skipped -- they wouldn't have type pages either. The
    /// returned graph is fully materialised into arrays so consumers
    /// can iterate by index without enumerator allocation.
    /// </summary>
    /// <param name="types">Types that received pages.</param>
    /// <returns>The ordered package -&gt; namespace -&gt; type graph.</returns>
    public NavigationGraph Build(ApiType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var options = _options;
        var byPackage = new Dictionary<string, Dictionary<string, List<NavigationEntry>>>(StringComparer.Ordinal);

        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var package = PackageRouter.ResolveFolder(type.AssemblyName, options.PackageRouting);
            if (package is null)
            {
                continue;
            }

            var nsKey = type.Namespace is [_, ..] ? type.Namespace : GlobalNamespaceKey;
            if (!byPackage.TryGetValue(package, out var byNs))
            {
                byNs = new(StringComparer.Ordinal);
                byPackage[package] = byNs;
            }

            if (!byNs.TryGetValue(nsKey, out var bucket))
            {
                bucket = [];
                byNs[nsKey] = bucket;
            }

            bucket.Add(new NavigationEntry(
                Title: ZensicalEmitterHelpers.FormatDisplayTypeName(type.Name, type.Arity),
                Path: ToPosixPath(TypePageEmitter.PathFor(type, options)),
                Kind: ClassifyKind(type),
                Arity: type.Arity,
                TypeParameters: type.TypeParameters));
        }

        var packageKeys = new string[byPackage.Count];
        byPackage.Keys.CopyTo(packageKeys, 0);
        Array.Sort(packageKeys, StringComparer.Ordinal);

        var packages = new NavigationPackage[packageKeys.Length];
        for (var p = 0; p < packageKeys.Length; p++)
        {
            packages[p] = MaterialisePackage(packageKeys[p], byPackage[packageKeys[p]]);
        }

        return new(packages);
    }

    /// <summary>
    /// Sorts the namespace and entry buckets for one package and
    /// freezes them into the immutable record-struct shape.
    /// </summary>
    /// <param name="packageName">Routed package folder name.</param>
    /// <param name="byNs">Namespace -&gt; entry-list buckets for the package.</param>
    /// <returns>The materialised package node.</returns>
    private static NavigationPackage MaterialisePackage(string packageName, Dictionary<string, List<NavigationEntry>> byNs)
    {
        var nsKeys = new string[byNs.Count];
        byNs.Keys.CopyTo(nsKeys, 0);
        Array.Sort(nsKeys, StringComparer.Ordinal);

        var namespaces = new NavigationNamespace[nsKeys.Length];
        for (var n = 0; n < nsKeys.Length; n++)
        {
            namespaces[n] = MaterialiseNamespace(nsKeys[n], byNs[nsKeys[n]]);
        }

        return new(packageName, namespaces);
    }

    /// <summary>
    /// Sorts the entries of one namespace by title and copies them
    /// into a fixed-size array.
    /// </summary>
    /// <param name="namespaceName">Namespace key (or <see cref="GlobalNamespaceKey"/>).</param>
    /// <param name="bucket">Unsorted entries collected for this namespace.</param>
    /// <returns>The materialised namespace node.</returns>
    private static NavigationNamespace MaterialiseNamespace(string namespaceName, List<NavigationEntry> bucket)
    {
        var entries = new NavigationEntry[bucket.Count];
        for (var i = 0; i < bucket.Count; i++)
        {
            entries[i] = bucket[i];
        }

        Array.Sort(entries, EntryByTitleComparer.Instance);
        return new(namespaceName, entries);
    }

    /// <summary>Normalises a path to forward slashes.</summary>
    /// <param name="path">A path produced by <see cref="Path.Combine(string, string)"/>.</param>
    /// <returns>The path with backslashes rewritten as forward slashes.</returns>
    private static string ToPosixPath(string path) =>
        path.IndexOf('\\') < 0 ? path : path.Replace('\\', '/');

    /// <summary>
    /// Maps an <see cref="ApiType"/> subtype + <see cref="ApiObjectType.Kind"/>
    /// onto the coarse <see cref="NavigationTypeKind"/> the nav graph
    /// surfaces. Pattern-matches the closed hierarchy so any new
    /// subtype added in the future fails fast at compile time when it
    /// reaches this switch (default arm throws).
    /// </summary>
    /// <param name="type">The type entering the nav graph.</param>
    /// <returns>The coarse nav-kind label.</returns>
    private static NavigationTypeKind ClassifyKind(ApiType type) => type switch
    {
        ApiObjectType { Kind: ApiObjectKind.Class } => NavigationTypeKind.Class,
        ApiObjectType { Kind: ApiObjectKind.Struct } => NavigationTypeKind.Struct,
        ApiObjectType { Kind: ApiObjectKind.Interface } => NavigationTypeKind.Interface,
        ApiObjectType { Kind: ApiObjectKind.Record } => NavigationTypeKind.Record,
        ApiObjectType { Kind: ApiObjectKind.RecordStruct } => NavigationTypeKind.RecordStruct,
        ApiEnumType => NavigationTypeKind.Enum,
        ApiDelegateType => NavigationTypeKind.Delegate,
        ApiUnionType => NavigationTypeKind.Union,
        _ => NavigationTypeKind.Class,
    };

    /// <summary>
    /// Ordinal title comparer for <see cref="NavigationEntry"/>.
    /// Singleton-cached so <see cref="Array.Sort{T}(T[], IComparer{T}?)"/>
    /// doesn't allocate a fresh comparer per namespace.
    /// </summary>
    private sealed class EntryByTitleComparer : IComparer<NavigationEntry>
    {
        /// <summary>The shared instance.</summary>
        public static readonly EntryByTitleComparer Instance = new();

        /// <summary>Initializes a new instance of the <see cref="EntryByTitleComparer"/> class.</summary>
        private EntryByTitleComparer()
        {
        }

        /// <inheritdoc />
        public int Compare(NavigationEntry x, NavigationEntry y) =>
            string.CompareOrdinal(x.Title, y.Title);
    }
}
