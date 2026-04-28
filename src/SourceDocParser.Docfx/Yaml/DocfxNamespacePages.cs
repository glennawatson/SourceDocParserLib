// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Common;
using SourceDocParser.Docfx.Common;
using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Composable helpers for emitting docfx namespace pages -- one
/// <c>Namespace.yml</c> per namespace, listing the types that live
/// directly in it as <c>children</c>. Docfx emits these alongside
/// every type page; without them an xrefmap build can't resolve
/// <c>N:Namespace</c> commentIds and the navigation tree drops the
/// namespace nodes. Each helper does one thing so the emitter
/// composes them and the tests can pin each in isolation.
/// </summary>
internal static class DocfxNamespacePages
{
    /// <summary>Suggested initial capacity for a namespace page builder.</summary>
    internal const int InitialPageCapacity = 512;

    /// <summary>The docfx <c>type</c> field value for a namespace item.</summary>
    private const string NamespaceTypeLabel = "Namespace";

    /// <summary>The docfx commentId prefix for a namespace.</summary>
    private const string NamespaceCommentIdPrefix = "N:";

    /// <summary>
    /// Groups <paramref name="types"/> by their namespace, drops the
    /// global namespace, and produces one <see cref="NamespacePage"/>
    /// per group. Caller iterates the array to emit pages.
    /// </summary>
    /// <param name="types">Catalog types to fold into namespace groups.</param>
    /// <returns>Sorted (by namespace) array of namespace pages.</returns>
    public static NamespacePage[] BuildNamespacePages(ApiType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (types is not [_, ..])
        {
            return [];
        }

        // Bucket-by-namespace in one pass so we visit each type once.
        var byNamespace = new Dictionary<string, (List<string> Uids, string AssemblyName)>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (type.Namespace is [])
            {
                continue;
            }

            if (CompilerGeneratedNames.IsCompilerGenerated(type.Name))
            {
                continue;
            }

            var uid = CommentIdPrefix.Strip(type.Uid is [_, ..] ? type.Uid : "T:" + type.FullName);
            if (uid is [])
            {
                continue;
            }

            if (!byNamespace.TryGetValue(type.Namespace, out var bucket))
            {
                bucket = ([], type.AssemblyName);
                byNamespace[type.Namespace] = bucket;
            }

            bucket.Uids.Add(uid);
        }

        var pages = new NamespacePage[byNamespace.Count];
        var idx = 0;
        foreach (var (ns, bucket) in byNamespace)
        {
            var uids = new string[bucket.Uids.Count];
            bucket.Uids.CopyTo(uids);
            Array.Sort(uids, StringComparer.Ordinal);
            pages[idx++] = new(ns, uids, bucket.AssemblyName);
        }

        Array.Sort(pages, static (a, b) => string.CompareOrdinal(a.Namespace, b.Namespace));
        return pages;
    }

    /// <summary>
    /// Returns the relative path for a namespace's <c>.yml</c> file.
    /// Mirrors docfx's <c>Namespace.yml</c> convention.
    /// </summary>
    /// <param name="namespaceName">Namespace whose page path to compute.</param>
    /// <returns>The path relative to the output root.</returns>
    public static string PathFor(string namespaceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        return DocfxInternalHelpers.SanitiseFileStem(namespaceName) + DocfxYamlEmitter.FileExtension;
    }

    /// <summary>
    /// Renders one namespace page as a YAML string -- header, single
    /// items entry with the namespace metadata + children list.
    /// References are emitted by the per-type pages, so the
    /// namespace page intentionally skips its own to keep the file
    /// compact (xrefmap builds still see the children list).
    /// </summary>
    /// <param name="page">Namespace page descriptor.</param>
    /// <returns>The YAML page text.</returns>
    public static string Render(in NamespacePage page)
    {
        using var rental = PageBuilderPool.Rent(InitialPageCapacity);
        BuildPage(rental.Builder, in page);
        return rental.Builder.ToString();
    }

    /// <summary>Composes the namespace page into <paramref name="sb"/>.</summary>
    /// <param name="sb">Destination builder; appended to in place.</param>
    /// <param name="page">Namespace page descriptor.</param>
    internal static void BuildPage(StringBuilder sb, in NamespacePage page)
    {
        ArgumentNullException.ThrowIfNull(page.Namespace);
        sb.AppendLine(DocfxYamlEmitter.YamlMimeHeader)
            .Append("items:\n")
            .Append("- uid: ").AppendScalar(page.Namespace).AppendLine()
            .Append("  commentId: ").Append(NamespaceCommentIdPrefix).AppendScalar(page.Namespace).AppendLine()
            .Append("  id: ").AppendScalar(page.Namespace).AppendLine()
            .Append("  children:\n");

        for (var i = 0; i < page.ChildUids.Length; i++)
        {
            sb.Append("  - ").AppendScalar(page.ChildUids[i]).AppendLine();
        }

        sb.AppendLine("  langs:")
            .AppendLine("  - csharp")
            .Append("  name: ").AppendScalar(page.Namespace).AppendLine()
            .Append("  nameWithType: ").AppendScalar(page.Namespace).AppendLine()
            .Append("  fullName: ").AppendScalar(page.Namespace).AppendLine()
            .Append("  type: ").AppendLine(NamespaceTypeLabel)
            .AppendLine("  assemblies:")
            .Append("  - ").AppendScalar(page.AssemblyName).AppendLine();
    }

    /// <summary>One entry per emitted namespace page.</summary>
    /// <param name="Namespace">Namespace name (never empty -- global namespace is skipped).</param>
    /// <param name="ChildUids">Bare UIDs of the types that live directly in this namespace, sorted ordinal.</param>
    /// <param name="AssemblyName">Assembly the namespace's types come from (first type's assembly).</param>
    public readonly record struct NamespacePage(string Namespace, string[] ChildUids, string AssemblyName);
}
