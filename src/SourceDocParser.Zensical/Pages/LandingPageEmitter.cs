// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using SourceDocParser.Model;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Renders the per-package and per-namespace <c>index.md</c> landing
/// pages that sit alongside the type pages in the api/ tree. Each
/// landing page is a directory listing: package landings link to
/// child namespaces, namespace landings link to child types.
/// Aggregation runs over the full type set in one pass to keep the
/// listings deterministic and grouped per package — namespaces with
/// the same name in different packages get their own page.
/// </summary>
public static class LandingPageEmitter
{
    /// <summary>Filename used for every landing page.</summary>
    [SuppressMessage("Critical Code Smell", "S2339:Public constant members should not be used", Justification = "Default value is not secret.")]
    public const string IndexFileName = "index.md";

    /// <summary>Initial StringBuilder capacity for a landing page.</summary>
    private const int InitialPageCapacity = 1024;

    /// <summary>
    /// Writes the package and namespace index pages for every
    /// (package, namespace) bucket implied by <paramref name="types"/>.
    /// Types from assemblies that don't resolve to a package folder
    /// are skipped — they wouldn't have type pages either.
    /// </summary>
    /// <param name="types">All types that received a type page.</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The number of landing pages written.</returns>
    public static int EmitAll(ApiType[] types, string outputRoot, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(options);

        var tree = BuildTree(types, options);
        var written = 0;
        foreach (var package in tree)
        {
            WritePackageIndex(outputRoot, package.Key, package.Value);
            written++;
            foreach (var ns in package.Value)
            {
                WriteNamespaceIndex(outputRoot, package.Key, ns.Key, ns.Value);
                written++;
            }
        }

        return written;
    }

    /// <summary>
    /// Render-and-write entry point used by
    /// <see cref="ZensicalDocumentationEmitter"/>. The landing page
    /// only consumes <see cref="ApiType.Documentation"/> via
    /// <c>OneLineSummary</c>, so this overload pre-renders each type's
    /// summary through the run's converter before delegating to
    /// <see cref="EmitAll(ApiType[], string, ZensicalEmitterOptions)"/>.
    /// </summary>
    /// <param name="types">All types that received a type page (raw-XML doc fragments).</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="context">Render context built once per emit run.</param>
    /// <returns>The number of landing pages written.</returns>
    internal static int EmitAll(ApiType[] types, string outputRoot, ZensicalEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(context);

        var rendered = new ApiType[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            rendered[i] = RenderedTypeFactory.Render(types[i], context.Converter);
        }

        return EmitAll(rendered, outputRoot, context.Options);
    }

    /// <summary>Writes the per-package index listing the package's namespaces.</summary>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="packageFolder">The package folder name.</param>
    /// <param name="namespaces">The package's namespace buckets.</param>
    private static void WritePackageIndex(
        string outputRoot,
        string packageFolder,
        SortedDictionary<string, List<TypeEntry>> namespaces)
    {
        var sb = new StringBuilder(capacity: InitialPageCapacity)
            .Append("# ").Append(packageFolder).AppendLine(" package")
            .AppendLine()
            .AppendLine("Namespaces in this package:")
            .AppendLine();

        foreach (var ns in namespaces)
        {
            var slug = NamespaceFolderName(ns.Key);
            sb.Append("- [").Append(ns.Key).Append("](").Append(slug).Append('/').Append(IndexFileName).Append(") — ")
              .Append(ns.Value.Count).AppendLine(" types");
        }

        var fullPath = Path.Combine(outputRoot, packageFolder, IndexFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, sb.ToString());
    }

    /// <summary>Writes the per-namespace index listing the namespace's types.</summary>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="packageFolder">The owning package folder name.</param>
    /// <param name="namespaceName">The display namespace.</param>
    /// <param name="entries">Types in this (package, namespace) bucket, pre-sorted.</param>
    private static void WriteNamespaceIndex(
        string outputRoot,
        string packageFolder,
        string namespaceName,
        List<TypeEntry> entries)
    {
        var sb = new StringBuilder(capacity: InitialPageCapacity)
            .Append("# ").Append(namespaceName).AppendLine(" namespace")
            .AppendLine()
            .Append("Part of the `").Append(packageFolder).AppendLine("` package.")
            .AppendLine()
            .AppendLine("| Type | Kind | Summary |")
            .AppendLine("| ---- | ---- | ------- |");

        foreach (var entry in entries)
        {
            sb.Append("| [").Append(entry.Title).Append("](").Append(entry.FileName).Append(") | ")
              .Append(entry.KindLabel).Append(" | ")
              .Append(entry.Summary).AppendLine(" |");
        }

        var folder = NamespaceFolderName(namespaceName);
        var fullPath = Path.Combine(outputRoot, packageFolder, folder, IndexFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, sb.ToString());
    }

    /// <summary>
    /// Buckets the supplied types into a package -&gt; namespace -&gt;
    /// entry tree using ordinal alphabetic ordering at every level.
    /// </summary>
    /// <param name="types">All types that received a type page.</param>
    /// <param name="options">Routing options used to derive the package folder.</param>
    /// <returns>The ordered tree.</returns>
    private static SortedDictionary<string, SortedDictionary<string, List<TypeEntry>>> BuildTree(
        ApiType[] types,
        ZensicalEmitterOptions options)
    {
        var tree = new SortedDictionary<string, SortedDictionary<string, List<TypeEntry>>>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var package = PackageRouter.ResolveFolder(type.AssemblyName, options.PackageRouting);
            if (package is null)
            {
                continue;
            }

            var ns = type.Namespace is [_, ..] ? type.Namespace : "(global)";

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

            entries.Add(new(
                Title: ZensicalEmitterHelpers.FormatDisplayTypeName(type.Name, type.Arity),
                FileName: ZensicalEmitterHelpers.FormatPathTypeName(type.Name, type.Arity) + TypePageEmitter.FileExtension,
                KindLabel: KindLabelFor(type),
                Summary: OneLineSummary(type.Documentation.Summary)));
        }

        foreach (var nsBucket in tree.Values)
        {
            foreach (var entries in nsBucket.Values)
            {
                entries.Sort(static (a, b) => string.CompareOrdinal(a.Title, b.Title));
            }
        }

        return tree;
    }

    /// <summary>Maps a namespace display name to its on-disk folder name.</summary>
    /// <param name="namespaceName">The display namespace.</param>
    /// <returns>The folder name (dots become directory separators; global namespace folds to <c>_global</c>).</returns>
    private static string NamespaceFolderName(string namespaceName) =>
        namespaceName == "(global)"
            ? "_global"
            : namespaceName.Replace('.', Path.DirectorySeparatorChar);

    /// <summary>Returns the human-readable kind label used in the namespace listing.</summary>
    /// <param name="type">The type to label.</param>
    /// <returns>The kind label.</returns>
    private static string KindLabelFor(ApiType type) => type switch
    {
        ApiObjectType { Kind: ApiObjectKind.Class } => "class",
        ApiObjectType { Kind: ApiObjectKind.Struct } => "struct",
        ApiObjectType { Kind: ApiObjectKind.Interface } => "interface",
        ApiObjectType { Kind: ApiObjectKind.Record } => "record",
        ApiObjectType { Kind: ApiObjectKind.RecordStruct } => "record struct",
        ApiEnumType => "enum",
        ApiDelegateType => "delegate",
        ApiUnionType => "union",
        _ => "type",
    };

    /// <summary>Collapses a documentation summary to a single, table-cell-safe line.</summary>
    /// <param name="summary">The raw documentation summary.</param>
    /// <returns>A single-line summary suitable for the listing table.</returns>
    private static string OneLineSummary(string summary)
        => ZensicalEmitterHelpers.FirstParagraphAsSingleLine(summary, escapePipes: true);

    /// <summary>One row in a namespace landing page.</summary>
    /// <param name="Title">Display name (with generic angles).</param>
    /// <param name="FileName">Type page filename, sibling of the index.md.</param>
    /// <param name="KindLabel">Short kind label (class / struct / record / interface / enum / delegate / union).</param>
    /// <param name="Summary">Single-line summary, table-escaped.</param>
    private readonly record struct TypeEntry(string Title, string FileName, string KindLabel, string Summary);
}
