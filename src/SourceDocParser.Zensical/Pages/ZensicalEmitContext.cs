// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Frozen;
using SourceDocParser.Model;
using SourceDocParser.XmlDoc;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Per-run rendering bundle threaded through the Zensical page
/// emitters. Holds everything that depends on the catalog being
/// emitted: routing options, catalog rollups, the set of UIDs that
/// will own a page, and the XML→Markdown converter wired up with the
/// matching <see cref="ZensicalCrefResolver"/>.
/// </summary>
/// <remarks>
/// Built once in <see cref="ZensicalDocumentationEmitter.EmitAsync(ApiType[], string, System.Threading.CancellationToken)"/>
/// and passed by reference to every page renderer so cref tags inside
/// doc strings resolve consistently across types and members.
/// </remarks>
internal sealed class ZensicalEmitContext
{
    /// <summary>Initializes a new instance of the <see cref="ZensicalEmitContext"/> class.</summary>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <param name="indexes">Catalog rollups computed once.</param>
    /// <param name="emittedUids">Set of UIDs (types + members) the emitter produces pages for.</param>
    /// <param name="converter">XML→Markdown converter wired with the cref resolver for this run.</param>
    public ZensicalEmitContext(
        ZensicalEmitterOptions options,
        ZensicalCatalogIndexes indexes,
        FrozenSet<string> emittedUids,
        XmlDocToMarkdown converter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(indexes);
        ArgumentNullException.ThrowIfNull(emittedUids);
        ArgumentNullException.ThrowIfNull(converter);
        Options = options;
        Indexes = indexes;
        EmittedUids = emittedUids;
        Converter = converter;
    }

    /// <summary>Gets the routing + cross-link tunables.</summary>
    public ZensicalEmitterOptions Options { get; }

    /// <summary>Gets the catalog rollups (derived classes / extensions / inherited members).</summary>
    public ZensicalCatalogIndexes Indexes { get; }

    /// <summary>Gets the UIDs the emitter is producing pages for.</summary>
    public FrozenSet<string> EmittedUids { get; }

    /// <summary>Gets the XML→Markdown converter wired with the cref resolver for this run.</summary>
    public XmlDocToMarkdown Converter { get; }

    /// <summary>
    /// Convenience: renders <paramref name="rawXml"/> through the
    /// bundled converter. Returns the empty string for a null or empty
    /// fragment so callers don't have to guard at every site.
    /// </summary>
    /// <param name="rawXml">Raw inner-XML fragment captured by the walker.</param>
    /// <returns>The rendered Markdown.</returns>
    public string RenderDocFragment(string? rawXml) =>
        rawXml is { Length: > 0 } ? Converter.Convert(rawXml) : string.Empty;
}
