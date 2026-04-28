// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using SourceDocParser.Model;
using SourceDocParser.XmlDoc;
using SourceDocParser.Zensical.Options;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Renders the per-package and per-namespace <c>index.md</c> landing
/// pages that sit alongside the type pages in the api/ tree. Each
/// landing page is a directory listing: package landings link to
/// child namespaces, namespace landings link to child types.
/// Aggregation runs over the full type set in one pass to keep the
/// listings deterministic and grouped per package -- namespaces with
/// the same name in different packages get their own page.
/// </summary>
internal static class LandingPageEmitter
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
    /// are skipped -- they wouldn't have type pages either.
    /// </summary>
    /// <param name="types">All types that received a type page.</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The number of landing pages written.</returns>
    public static int EmitAll(ApiType[] types, string outputRoot, ZensicalEmitterOptions options) =>
        EmitAll(types, outputRoot, BuildDefaultConverter(), options);

    /// <summary>
    /// Production landing-page emit path that consumes raw walker
    /// output and converts each type's summary on demand via the
    /// supplied converter -- one Convert call per type-page entry,
    /// no record allocation.
    /// </summary>
    /// <param name="types">All types that received a type page -- raw walker output.</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="converter">XML to Markdown converter wired with the emitter's <see cref="ICrefResolver"/>.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The number of landing pages written.</returns>
    internal static int EmitAll(ApiType[] types, string outputRoot, XmlDocToMarkdown converter, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(options);

        var tree = BuildTree(types, converter, options);
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
    /// <see cref="ZensicalDocumentationEmitter"/>. Threads the run's
    /// converter into the lazy summary materialisation so the landing
    /// pages share the same per-symbol XML to Markdown rendering pass
    /// as the type pages without rebuilding catalog records.
    /// </summary>
    /// <param name="types">All types that received a type page -- raw walker output.</param>
    /// <param name="outputRoot">The directory that contains the api/ tree.</param>
    /// <param name="context">Render context built once per emit run.</param>
    /// <returns>The number of landing pages written.</returns>
    internal static int EmitAll(ApiType[] types, string outputRoot, ZensicalEmitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return EmitAll(types, outputRoot, context.Converter, context.Options);
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
        using var rental = PageBuilderPool.Rent(InitialPageCapacity);
        var sb = rental.Builder
            .Append("# ").Append(packageFolder).AppendLine(" package")
            .AppendLine()
            .AppendLine("Namespaces in this package:")
            .AppendLine();

        foreach (var ns in namespaces)
        {
            var slug = NamespaceFolderName(ns.Key);
            sb.Append("- [").Append(ns.Key).Append("](").Append(slug).Append('/').Append(IndexFileName).Append(") -- ")
              .Append(ns.Value.Count).AppendLine(" types");
        }

        PageWriter.WriteUtf8(Path.Combine(outputRoot, packageFolder, IndexFileName), sb);
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
        using var rental = PageBuilderPool.Rent(InitialPageCapacity);
        var sb = rental.Builder
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
        PageWriter.WriteUtf8(Path.Combine(outputRoot, packageFolder, folder, IndexFileName), sb);
    }

    /// <summary>
    /// Buckets the supplied types into a package -> namespace ->
    /// entry tree using ordinal alphabetic ordering at every level.
    /// Delegates the bucketing loop to the shared
    /// <see cref="PackageNamespaceTreeBuilder.Build"/> helper so the
    /// tree-building logic stays in one place.
    /// </summary>
    /// <param name="types">All types that received a type page.</param>
    /// <param name="converter">XML to Markdown converter for the per-type summary.</param>
    /// <param name="options">Routing options used to derive the package folder.</param>
    /// <returns>The ordered tree.</returns>
    private static SortedDictionary<string, SortedDictionary<string, List<TypeEntry>>> BuildTree(
        ApiType[] types,
        XmlDocToMarkdown converter,
        ZensicalEmitterOptions options) =>
        PackageNamespaceTreeBuilder.Build(
            types,
            options.PackageRouting,
            type => new TypeEntry(
                Title: ZensicalEmitterHelpers.FormatDisplayTypeName(type.Name, type.Arity),
                FileName: ZensicalEmitterHelpers.FormatPathTypeName(type.Name, type.Arity) + TypePageEmitter.FileExtension,
                KindLabel: KindLabelFor(type),
                Summary: OneLineSummary(converter.Convert(type.Documentation.Summary))),
            static (a, b) => string.CompareOrdinal(a.Title, b.Title));

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

    /// <summary>
    /// Constructs the default per-call <see cref="XmlDocToMarkdown"/> the
    /// converter-less <c>EmitAll</c> overload uses. Production emit
    /// paths supply their own resolver-aware converter.
    /// </summary>
    /// <returns>A fresh converter; cheap, single-purpose, not cached.</returns>
    private static XmlDocToMarkdown BuildDefaultConverter() => new(DefaultCrefResolver.Instance);

    /// <summary>One row in a namespace landing page.</summary>
    /// <param name="Title">Display name (with generic angles).</param>
    /// <param name="FileName">Type page filename, sibling of the index.md.</param>
    /// <param name="KindLabel">Short kind label (class / struct / record / interface / enum / delegate / union).</param>
    /// <param name="Summary">Single-line summary, table-escaped.</param>
    private readonly record struct TypeEntry(string Title, string FileName, string KindLabel, string Summary);
}
