// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Forward-only scanner over a span of XML doc text. Replaces XmlReader
/// on the doc-parse hot path — every XmlReader.Create instantiated an
/// XmlTextReaderImpl with multi-KB internal buffers (NodeData arrays,
/// NamespaceManager, char buffers, Entry arrays) per symbol, which
/// dominated the post-streaming allocation profile (~18% of the
/// LoadAndWalk budget).
/// </summary>
/// <remarks>
/// Implements only the slice of the XML grammar that .NET XML doc files
/// actually use: element start/end tags (with optional self-closing
/// suffix), attributes with double-quoted values, text content with the
/// five standard entities (lt, gt, amp, quot, apos) and numeric
/// character references in decimal or hex form, comments and
/// processing instructions (skipped), and CDATA sections (surfaced as
/// text). Single-quoted attributes, DTDs, internal subsets, mixed
/// encodings, and other production-XML edge cases are out of scope —
/// the .NET XML doc emitter never produces them.
/// </remarks>
internal ref struct DocXmlScanner
{
    /// <summary>Raw text being scanned.</summary>
    private readonly ReadOnlySpan<char> _input;

    /// <summary>Current read position into the input.</summary>
    private int _pos;

    /// <summary>Slice of the start tag's attribute area (between the element name and the tag closer).</summary>
    private ReadOnlySpan<char> _attrArea;

    /// <summary>Initializes a new instance of the <see cref="DocXmlScanner"/> struct.</summary>
    /// <param name="input">XML text to scan.</param>
    public DocXmlScanner(ReadOnlySpan<char> input)
    {
        _input = input;
        _pos = 0;
        Kind = DocTokenKind.None;
        Depth = 0;
        IsEmptyElement = false;
        TokenStart = 0;
    }

    /// <summary>Gets the start position of the most recent token in the input span (the offset of the opening character for markup, or the first character for text).</summary>
    public int TokenStart { get; private set; }

    /// <summary>Gets the kind of the most recent token.</summary>
    public DocTokenKind Kind { get; private set; }

    /// <summary>Gets the depth of the current element (root start element is at depth 1).</summary>
    public int Depth { get; private set; }

    /// <summary>Gets a value indicating whether the most recent start element was self-closing.</summary>
    public bool IsEmptyElement { get; private set; }

    /// <summary>Gets the element name for the current start/end token. Caller-side SequenceEqual against literals avoids allocating per check.</summary>
    public ReadOnlySpan<char> Name { get; private set; }

    /// <summary>Gets the raw text slice for the current text token (entity-encoded).</summary>
    public ReadOnlySpan<char> RawText { get; private set; }

    /// <summary>
    /// Advances to the next significant token. Returns false when the
    /// input is exhausted. Comments and processing instructions are
    /// silently consumed.
    /// </summary>
    /// <returns>True when a new token is available.</returns>
    public bool Read()
    {
        IsEmptyElement = false;

        if (_pos >= _input.Length)
        {
            Kind = DocTokenKind.None;
            return false;
        }

        var ch = _input[_pos];
        if (ch == '<')
        {
            TokenStart = _pos;
            var result = XmlMarkupParser.ReadMarkup(_input, _pos);
            _pos = result.NewPos;
            if (!result.Success)
            {
                Kind = DocTokenKind.None;
                return false;
            }

            if (result.IsSilent)
            {
                return Read();
            }

            Kind = result.Kind;
            Name = result.Name;
            RawText = result.RawText;
            _attrArea = result.AttrArea;
            IsEmptyElement = result.IsEmptyElement;

            if (Kind == DocTokenKind.EndElement)
            {
                Depth--;
            }
            else if (Kind == DocTokenKind.StartElement)
            {
                Depth += IsEmptyElement ? 0 : 1;
            }
            else
            {
                // No depth change for other token kinds.
            }

            return true;
        }

        // Text run up to the next '<'.
        TokenStart = _pos;
        var nextLt = _input[_pos..].IndexOf('<');
        var end = nextLt < 0 ? _input.Length : _pos + nextLt;
        RawText = _input[_pos..end];
        _pos = end;
        Kind = DocTokenKind.Text;
        return true;
    }

    /// <summary>
    /// Captures the raw inner XML span of the current element (the
    /// content between its start tag and the matching end tag) and
    /// advances the scanner past the end element. Must be called when
    /// positioned on a start element.
    /// </summary>
    /// <returns>The inner XML span (still entity-encoded, may contain nested elements). Empty for self-closing elements.</returns>
    public ReadOnlySpan<char> ReadInnerSpan()
    {
        if (IsEmptyElement)
        {
            return default;
        }

        var startDepth = Depth;
        var innerStart = _pos;
        while (Read())
        {
            if (Kind == DocTokenKind.EndElement && Depth < startDepth)
            {
                return _input[innerStart..TokenStart];
            }
        }

        return default;
    }

    /// <summary>
    /// Returns the value of <paramref name="attributeName"/> on the
    /// current start tag, or an empty span when not present. Only
    /// double-quoted values are supported (matches every .NET XML doc
    /// emitter).
    /// </summary>
    /// <param name="attributeName">Attribute name to look up.</param>
    /// <returns>The attribute value as a span over the source, or empty.</returns>
    public readonly ReadOnlySpan<char> GetAttribute(ReadOnlySpan<char> attributeName) => XmlAttributeParser.GetAttribute(_attrArea, attributeName);

    /// <summary>
    /// Advances the scanner past the current element, ignoring its
    /// children. The scanner must be positioned on a start element;
    /// on return it is positioned on the matching end element (or
    /// stays put for self-closing elements).
    /// </summary>
    public void SkipElement()
    {
        if (IsEmptyElement)
        {
            return;
        }

        var startDepth = Depth;
        while (Read())
        {
            if (Kind == DocTokenKind.EndElement && Depth < startDepth)
            {
                return;
            }
        }
    }

    /// <summary>True for the four whitespace characters allowed inside an XML start tag.</summary>
    /// <param name="ch">Character to test.</param>
    /// <returns>True when whitespace.</returns>
    internal static bool IsWhitespace(char ch) => XmlCharHelper.IsWhitespace(ch);
}
