// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Hand-rolled XmlReader scanner for the
/// <c>packageSources</c> section of a <c>nuget.config</c>
/// -- same composable shape as
/// <see cref="NuGetConfigReader"/> and
/// <see cref="NuspecDependencyReader"/>. Returns the per-file
/// view (the ordered list of <c>add</c> entries plus
/// whether a <c>clear/</c> wiped the accumulator) so the
/// discovery walk can layer cross-file merge semantics on top.
/// </summary>
internal static class PackageSourcesReader
{
    /// <summary>NuGet's container element for the source list.</summary>
    private const string PackageSourcesElementName = "packageSources";

    /// <summary>Per-source entry inside <c>packageSources</c>.</summary>
    private const string AddElementName = "add";

    /// <summary>Wipes the accumulator within the file (and parent values during cross-file merge).</summary>
    private const string ClearElementName = "clear";

    /// <summary>Attribute on <c>add</c> carrying the friendly source name.</summary>
    private const string KeyAttributeName = "key";

    /// <summary>Attribute on <c>add</c> carrying the source URL.</summary>
    private const string ValueAttributeName = "value";

    /// <summary>Reader settings shared across every parse -- async on so we pump a FileStream that opened with FileOptions.Asynchronous.</summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        Async = true, IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Reads the <c>packageSources</c> section from
    /// <paramref name="configPath"/>.
    /// </summary>
    /// <param name="configPath">Absolute path to a <c>nuget.config</c>.</param>
    /// <returns>Per-file result (clearedSeen + ordered sources).</returns>
    public static Task<PackageSourceFileResult> ReadPackageSourcesAsync(string configPath) =>
        ReadPackageSourcesAsync(configPath, CancellationToken.None);

    /// <summary>
    /// Reads the <c>packageSources</c> section from
    /// <paramref name="configPath"/>. Returns
    /// <see cref="PackageSourceFileResult.ClearedSeen"/> alongside the
    /// post-clear (or post-no-clear) entries so the discovery
    /// walk can stop chaining to less-specific configs after a
    /// clear.
    /// </summary>
    /// <param name="configPath">Absolute path to a <c>nuget.config</c>.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>Per-file result (clearedSeen + ordered sources).</returns>
    public static async Task<PackageSourceFileResult> ReadPackageSourcesAsync(
        string configPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var stream = new FileStream(
            configPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadPackageSourcesAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the <c>packageSources</c> section from an open
    /// <c>nuget.config</c> stream.
    /// </summary>
    /// <param name="configStream">Open stream positioned at the start of the <c>nuget.config</c> XML.</param>
    /// <returns>Per-file result (clearedSeen + ordered sources).</returns>
    public static Task<PackageSourceFileResult> ReadPackageSourcesAsync(Stream configStream) =>
        ReadPackageSourcesAsync(configStream, CancellationToken.None);

    /// <summary>
    /// Stream-based overload -- useful for tests that feed canned
    /// XML without a tempfile dance.
    /// </summary>
    /// <param name="configStream">Open stream positioned at the start of the <c>nuget.config</c> XML.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>Per-file result (clearedSeen + ordered sources).</returns>
    public static async Task<PackageSourceFileResult> ReadPackageSourcesAsync(
        Stream configStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configStream);

        // Per NuGet's SettingFactory.ParseChildren: walk the entire
        // section in document order. <clear/> wipes the accumulator
        // (and signals to the caller that any parent-supplied
        // entries should also be wiped during the cross-file merge).
        // Within a file, the FIRST <add> for a given key wins --
        // duplicates are silently dropped (NuGet semantics).
        using var reader = XmlReader.Create(configStream, _readerSettings);
        var insideSection = false;
        var clearedSeen = false;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PackageSource>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryUpdatePackageSourcesScope(reader, ref insideSection) || !ShouldInspectPackageSourcesElement(reader, insideSection))
            {
                continue;
            }

            if (HandlePackageSourcesClear(reader, entries, seenKeys, ref clearedSeen))
            {
                continue;
            }

            TryAddPackageSource(reader, seenKeys, entries);
        }

        return new(clearedSeen, [.. entries]);
    }

    /// <summary>
    /// Updates whether the reader is currently inside the packageSources section.
    /// </summary>
    /// <param name="reader">Reader positioned on the current node.</param>
    /// <param name="insideSection">Current in-section flag.</param>
    /// <returns>True when the node only updated scope.</returns>
    internal static bool TryUpdatePackageSourcesScope(XmlReader reader, ref bool insideSection)
    {
        if (reader is { NodeType: XmlNodeType.Element }
            && reader.LocalName.Equals(PackageSourcesElementName, StringComparison.OrdinalIgnoreCase))
        {
            insideSection = true;
            return true;
        }

        if (reader is not { NodeType: XmlNodeType.EndElement }
            || !reader.LocalName.Equals(PackageSourcesElementName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        insideSection = false;
        return true;
    }

    /// <summary>
    /// Returns true when the current node should be inspected as a packageSources child element.
    /// </summary>
    /// <param name="reader">Reader positioned on the current node.</param>
    /// <param name="insideSection">Whether the parser is currently inside the packageSources section.</param>
    /// <returns>True when the node is a candidate packageSources child element.</returns>
    internal static bool ShouldInspectPackageSourcesElement(XmlReader reader, bool insideSection) =>
        insideSection && reader.NodeType == XmlNodeType.Element;

    /// <summary>
    /// Handles a clear directive inside the packageSources section.
    /// </summary>
    /// <param name="reader">Reader positioned on the current element.</param>
    /// <param name="entries">Current entry accumulator.</param>
    /// <param name="seenKeys">Current duplicate-key filter set.</param>
    /// <param name="clearedSeen">Whether a clear directive has been seen.</param>
    /// <returns>True when the element was a clear directive.</returns>
    internal static bool HandlePackageSourcesClear(
        XmlReader reader,
        List<PackageSource> entries,
        HashSet<string> seenKeys,
        ref bool clearedSeen)
    {
        if (!reader.LocalName.Equals(ClearElementName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        entries.Clear();
        seenKeys.Clear();
        clearedSeen = true;
        return true;
    }

    /// <summary>
    /// Adds a package source entry when the current element is a valid, non-duplicate add entry.
    /// </summary>
    /// <param name="reader">Reader positioned on the current element.</param>
    /// <param name="seenKeys">Duplicate-key filter set.</param>
    /// <param name="entries">Current entry accumulator.</param>
    internal static void TryAddPackageSource(
        XmlReader reader,
        HashSet<string> seenKeys,
        List<PackageSource> entries)
    {
        if (!reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = reader.GetAttribute(KeyAttributeName);
        if (!TextHelpers.HasNonWhitespace(key))
        {
            return;
        }

        var value = reader.GetAttribute(ValueAttributeName);
        if (!TextHelpers.HasNonWhitespace(value))
        {
            return;
        }

        if (!seenKeys.Add(key))
        {
            return;
        }

        entries.Add(new(key, value));
    }
}
