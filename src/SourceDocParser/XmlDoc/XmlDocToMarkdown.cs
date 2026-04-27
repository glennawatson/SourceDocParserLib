// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using System.Xml;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Converts .NET XML documentation fragments into Markdown.
/// </summary>
/// <remarks>
/// Driven by DocXmlScanner — a span-based forward scanner — instead of
/// XmlReader, so the conversion path no longer allocates an
/// XmlTextReaderImpl with its multi-KB buffers per call. Handles the
/// standard inline doc tags: see, seealso, paramref, typeparamref, c,
/// code, para, b/strong, i/em, br, list (bullet/number/table), item,
/// term, description. Unknown tags fall through to their inner content
/// so nothing is silently dropped.
/// </remarks>
public sealed class XmlDocToMarkdown : IXmlDocToMarkdownConverter
{
    /// <summary>Initial StringBuilder capacity for tagged conversions; trimmed back as needed.</summary>
    private const int InitialBuilderCapacity = 256;

    /// <summary>
    /// Pooled StringBuilder reused across top-level Convert calls so each
    /// per-symbol render doesn't allocate a fresh one. Convert() calls are
    /// always sequential within a single converter instance because each
    /// DocResolver creates its own (DocResolver itself is per-walk and
    /// single-threaded), so no synchronisation is needed.
    /// </summary>
    private readonly StringBuilder _builder = new(InitialBuilderCapacity);

    /// <inheritdoc />
    public string Convert(string xmlFragment) => Convert(xmlFragment.AsSpan());

    /// <inheritdoc />
    public string Convert(ReadOnlySpan<char> innerXml)
    {
        if (innerXml.IsEmpty || innerXml.IsWhiteSpace())
        {
            return string.Empty;
        }

        _builder.Clear();

        // Plain-text fast path: no '<' means no inline tags to render,
        // just decode standard entities. Skips even the scanner overhead.
        if (innerXml.IndexOf('<') < 0)
        {
            XmlEntityDecoder.AppendDecoded(_builder, innerXml);
            return _builder.ToString();
        }

        var scanner = new DocXmlScanner(innerXml);
        XmlDocMarkdownHelper.WriteFragment(ref scanner, _builder, ListContext.None);
        return XmlDocMarkdownHelper.CollapseWhitespace(_builder).ToString();
    }

    /// <inheritdoc />
    public Task<string> ConvertAsync(XmlReader reader) => ConvertAsync(reader, CancellationToken.None);

    /// <inheritdoc />
    public async Task<string> ConvertAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        var innerXml = await ReadInnerXmlAsync(reader, cancellationToken).ConfigureAwait(false);
        return Convert(innerXml.AsSpan());
    }

    /// <summary>
    /// Reads the inner XML content asynchronously from an <see cref="XmlReader"/> instance.
    /// </summary>
    /// <param name="reader">The <see cref="XmlReader"/> instance from which to read the inner XML content.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with a result of the inner XML content as a string.</returns>
    internal static async Task<string> ReadInnerXmlAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement)
        {
            return string.Empty;
        }

        // Materialise the inner XML once and route through the scanner-
        // based renderer. Callers that already have an XmlReader open
        // (rare since DocResolver moved to the scanner) pay one extra
        // string allocation; the alternative — keeping a parallel
        // XmlReader-based renderer — is more code with no real win.
        return await reader.ReadInnerXmlAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Renders a captured inner span into Markdown via a fresh scanner.
    /// Internal so <see cref="MarkdownListTableRenderer"/> can recurse
    /// into nested term/description content without going through the
    /// instance-bound public Convert entry point.
    /// </summary>
    /// <param name="span">Inner XML span to render.</param>
    /// <returns>Markdown text.</returns>
    internal static string ConvertSpanToMarkdown(in ReadOnlySpan<char> span) =>
        XmlDocMarkdownHelper.ConvertSpanToMarkdown(span);

    /// <summary>
    /// Escapes pipes and replaces newlines with spaces so a string is
    /// safe to drop into a GFM table cell. Internal so
    /// <see cref="MarkdownListTableRenderer"/> can apply the same
    /// escape to its term/description columns.
    /// </summary>
    /// <param name="text">Cell content.</param>
    /// <returns>The escaped text, or a single space when the input was empty.</returns>
    internal static string TableEscape(string text) =>
        XmlDocMarkdownHelper.TableEscape(text);
}
