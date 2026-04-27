// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Readers;

/// <summary>
/// Hand-rolled XmlReader scanner for the
/// <c>&lt;packageSources&gt;</c> section of a <c>nuget.config</c>
/// — same composable shape as
/// <see cref="NuGetConfigReader"/> and
/// <see cref="NuspecDependencyReader"/>. Returns the per-file
/// view (the ordered list of <c>&lt;add&gt;</c> entries plus
/// whether a <c>&lt;clear/&gt;</c> wiped the accumulator) so the
/// discovery walk can layer cross-file merge semantics on top.
/// </summary>
internal static class PackageSourcesReader
{
    /// <summary>NuGet's container element for the source list.</summary>
    private const string PackageSourcesElementName = "packageSources";

    /// <summary>Per-source entry inside <c>&lt;packageSources&gt;</c>.</summary>
    private const string AddElementName = "add";

    /// <summary>Wipes the accumulator within the file (and parent values during cross-file merge).</summary>
    private const string ClearElementName = "clear";

    /// <summary>Attribute on <c>&lt;add&gt;</c> carrying the friendly source name.</summary>
    private const string KeyAttributeName = "key";

    /// <summary>Attribute on <c>&lt;add&gt;</c> carrying the source URL.</summary>
    private const string ValueAttributeName = "value";

    /// <summary>Reader settings shared across every parse — async on so we pump a FileStream that opened with FileOptions.Asynchronous.</summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        Async = true, IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Reads the <c>&lt;packageSources&gt;</c> section from
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
        CancellationToken cancellationToken = default)
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
    /// Stream-based overload — useful for tests that feed canned
    /// XML without a tempfile dance.
    /// </summary>
    /// <param name="configStream">Open stream positioned at the start of the <c>nuget.config</c> XML.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>Per-file result (clearedSeen + ordered sources).</returns>
    public static async Task<PackageSourceFileResult> ReadPackageSourcesAsync(
        Stream configStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configStream);

        // Per NuGet's SettingFactory.ParseChildren: walk the entire
        // section in document order. <clear/> wipes the accumulator
        // (and signals to the caller that any parent-supplied
        // entries should also be wiped during the cross-file merge).
        // Within a file, the FIRST <add> for a given key wins —
        // duplicates are silently dropped (NuGet semantics).
        using var reader = XmlReader.Create(configStream, _readerSettings);
        var insideSection = false;
        var clearedSeen = false;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PackageSource>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader is { NodeType: XmlNodeType.Element } &&
                reader.LocalName.Equals(PackageSourcesElementName, StringComparison.OrdinalIgnoreCase))
            {
                insideSection = true;
                continue;
            }

            if (reader is { NodeType: XmlNodeType.EndElement } &&
                reader.LocalName.Equals(PackageSourcesElementName, StringComparison.OrdinalIgnoreCase))
            {
                insideSection = false;
                continue;
            }

            if (!insideSection || reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName.Equals(ClearElementName, StringComparison.OrdinalIgnoreCase))
            {
                entries.Clear();
                seenKeys.Clear();
                clearedSeen = true;
                continue;
            }

            if (!reader.LocalName.Equals(AddElementName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = reader.GetAttribute(KeyAttributeName);
            var value = reader.GetAttribute(ValueAttributeName);
            if (!TextHelpers.HasNonWhitespace(key) || !TextHelpers.HasNonWhitespace(value))
            {
                continue;
            }

            if (!seenKeys.Add(key))
            {
                continue;
            }

            entries.Add(new(key, value));
        }

        return new(clearedSeen, [.. entries]);
    }
}
