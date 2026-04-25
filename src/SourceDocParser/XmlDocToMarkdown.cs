// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using System.Xml;

namespace SourceDocParser;

/// <summary>
/// Converts .NET XML documentation fragments into Markdown.
/// </summary>
/// <remarks>
/// This class handles standard inline doc tags like see, seealso, paramref, typeparamref,
/// c, code, para, b, i, br, and list. Unknown tags have their inner content emitted verbatim.
///
/// Performance is optimized using a streaming <see cref="XmlReader"/>, single pass,
/// and minimal allocations. Results are typically memoized by callers.
/// </remarks>
public sealed class XmlDocToMarkdown : IXmlDocToMarkdownConverter
{
    /// <summary>
    /// Shared XML reader settings.
    /// </summary>
    private static readonly XmlReaderSettings _settings = new()
    {
        ConformanceLevel = ConformanceLevel.Fragment,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Ignore,
        CheckCharacters = false,
    };

    /// <inheritdoc />
    public string Convert(string xmlFragment) => ConvertString(xmlFragment);

    /// <inheritdoc />
    public string Convert(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement)
        {
            return string.Empty;
        }

        // Walk only the children of the current element. WriteSubtreeNodes
        // stops when it sees the matching end element at startDepth, so
        // the caller's reader is left positioned on that end element —
        // the same place reader.ReadInnerXml() would have left it.
        var startDepth = reader.Depth;
        var sb = new StringBuilder(256);
        WriteSubtreeNodes(reader, sb, ListContext.None, startDepth);
        return CollapseWhitespace(sb).ToString();
    }

    /// <inheritdoc />
    public string Convert(ReadOnlySpan<char> innerXml)
    {
        if (innerXml.IsEmpty)
        {
            return string.Empty;
        }

        // Plain-text fast path: no '<' means no inline tags to render,
        // just decode standard entities into a StringBuilder. Skips the
        // XmlReader.Create + XmlTextReaderImpl allocation chain.
        if (innerXml.IndexOf('<') < 0)
        {
            var plain = new StringBuilder(innerXml.Length);
            DocXmlScanner.AppendDecoded(plain, innerXml);
            return plain.ToString();
        }

        // Tagged content: defer to the string-based renderer until the
        // scanner-native markdown pipeline lands.
        return ConvertString(innerXml.ToString());
    }

    /// <summary>
    /// Static implementation of <see cref="Convert(string)"/>. Kept
    /// separate so the few internal static helpers that need to convert
    /// a captured fragment (e.g. <c>ReadTermAndDescription</c>) can call
    /// it without going through an instance.
    /// </summary>
    /// <param name="xmlFragment">Inner XML of one doc element.</param>
    /// <returns>Markdown-formatted doc fragment, or an empty string.</returns>
    private static string ConvertString(string xmlFragment)
    {
        if (string.IsNullOrWhiteSpace(xmlFragment))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(xmlFragment.Length);
        using var stringReader = new StringReader(xmlFragment);
        using var reader = XmlReader.Create(stringReader, _settings);

        WriteNodes(reader, sb, ListContext.None);

        return CollapseWhitespace(sb).ToString();
    }

    /// <summary>
    /// Streaming variant of <see cref="WriteNodes"/> that stops at the
    /// matching end element instead of EOF. Used by <see cref="Convert(XmlReader)"/>
    /// to consume just the current element's subtree.
    /// </summary>
    /// <param name="reader">Active reader positioned on a start element.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    /// <param name="startDepth">Depth of the parent start element; iteration stops at <see cref="XmlNodeType.EndElement"/> at this depth.</param>
    private static void WriteSubtreeNodes(XmlReader reader, StringBuilder sb, ListContext listContext, int startDepth)
    {
        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.EndElement && reader.Depth == startDepth)
            {
                return;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    {
                        sb.Append(reader.Value);
                        break;
                    }

                case XmlNodeType.Element:
                    {
                        WriteElement(reader, sb, listContext);
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Drives the read loop until end-of-fragment.
    /// </summary>
    /// <param name="reader">Active reader.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Current list scope context.</param>
    private static void WriteNodes(XmlReader reader, StringBuilder sb, ListContext listContext)
    {
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    {
                        sb.Append(reader.Value);
                        break;
                    }

                case XmlNodeType.Element:
                    {
                        WriteElement(reader, sb, listContext);
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Dispatches one element to its writer.
    /// </summary>
    /// <param name="reader">Reader positioned on an element start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    private static void WriteElement(XmlReader reader, StringBuilder sb, ListContext listContext)
    {
        switch (reader.Name)
        {
            case "see":
                {
                    WriteSee(reader, sb);
                    break;
                }

            case "seealso":
                {
                    // Element-level seealso entries are surfaced separately by
                    // ApiDocumentation; render inline ones as a plain ref link.
                    WriteSee(reader, sb);
                    break;
                }

            case "paramref":
            case "typeparamref":
                {
                    if (reader.GetAttribute("name") is { Length: > 0 } refName)
                    {
                        sb.Append('`').Append(refName).Append('`');
                    }

                    ConsumeElement(reader);
                    break;
                }

            case "c":
                {
                    sb.Append('`');
                    WriteChildren(reader, sb, listContext, suppressTags: true);
                    sb.Append('`');
                    break;
                }

            case "code":
                {
                    WriteCode(reader, sb);
                    break;
                }

            case "para":
                {
                    EnsureBlankLine(sb);
                    WriteChildren(reader, sb, listContext, suppressTags: false);
                    EnsureBlankLine(sb);
                    break;
                }

            case "br":
                {
                    sb.Append("  \n");
                    ConsumeElement(reader);
                    break;
                }

            case "b":
            case "strong":
                {
                    sb.Append("**");
                    WriteChildren(reader, sb, listContext, suppressTags: false);
                    sb.Append("**");
                    break;
                }

            case "i":
            case "em":
                {
                    sb.Append('*');
                    WriteChildren(reader, sb, listContext, suppressTags: false);
                    sb.Append('*');
                    break;
                }

            case "list":
                {
                    WriteList(reader, sb);
                    break;
                }

            case "item":
                {
                    // Bare item outside <list>: render as a bullet so we
                    // don't lose the content even if the doc author was
                    // sloppy with the wrapping element.
                    EnsureLineStart(sb);
                    sb.Append("- ");
                    WriteChildren(reader, sb, ListContext.Bullet, suppressTags: false);
                    EnsureLineStart(sb);
                    break;
                }

            case "description":
            case "term":
                {
                    // List item parts: emit children inline; the parent
                    // <list> writer arranges separators.
                    WriteChildren(reader, sb, listContext, suppressTags: false);
                    break;
                }

            default:
                {
                    // Unknown tag: keep its content so we don't drop words.
                    WriteChildren(reader, sb, listContext, suppressTags: false);
                    break;
                }
        }
    }

    /// <summary>
    /// Renders a see or seealso reference.
    /// </summary>
    /// <param name="reader">Reader positioned on the see/seealso start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteSee(XmlReader reader, StringBuilder sb)
    {
        if (reader.GetAttribute("cref") is { Length: > 0 } cref)
        {
            var shortName = ShortName(cref);
            sb.Append('[').Append(shortName).Append("][").Append(cref).Append(']');
        }
        else if (reader.GetAttribute("langword") is { Length: > 0 } langword)
        {
            sb.Append('`').Append(langword).Append('`');
        }
        else if (reader.GetAttribute("href") is { Length: > 0 } href)
        {
            // Walk the children for the link text, fall back to href
            // when the element is empty.
            if (reader.IsEmptyElement)
            {
                sb.Append('<').Append(href).Append('>');
                return;
            }

            sb.Append('[');
            WriteChildren(reader, sb, ListContext.None, suppressTags: true);
            sb.Append("](").Append(href).Append(')');
            return;
        }

        ConsumeElement(reader);
    }

    /// <summary>
    /// Renders a code block as fenced C#.
    /// </summary>
    /// <param name="reader">Reader positioned on the code start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteCode(XmlReader reader, StringBuilder sb)
    {
        if (reader.IsEmptyElement)
        {
            ConsumeElement(reader);
            return;
        }

        var content = reader.ReadInnerXml();
        EnsureBlankLine(sb);
        sb.Append("```csharp\n").Append(content.Trim()).Append("\n```\n");
    }

    /// <summary>
    /// Renders a list based on its type.
    /// </summary>
    /// <param name="reader">Reader positioned on the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteList(XmlReader reader, StringBuilder sb)
    {
        if (reader.IsEmptyElement)
        {
            ConsumeElement(reader);
            return;
        }

        var type = reader.GetAttribute("type") ?? "bullet";
        EnsureBlankLine(sb);

        if (string.Equals(type, "table", StringComparison.OrdinalIgnoreCase))
        {
            WriteListAsTable(reader, sb);
        }
        else
        {
            WriteListAsBulletsOrNumbered(reader, sb, type);
        }

        EnsureBlankLine(sb);
    }

    /// <summary>
    /// Bullet or numbered list path.
    /// </summary>
    /// <param name="reader">Reader positioned just inside the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="type">List type from the XML attribute.</param>
    private static void WriteListAsBulletsOrNumbered(XmlReader reader, StringBuilder sb, string type)
    {
        var numbered = string.Equals(type, "number", StringComparison.OrdinalIgnoreCase);
        var index = 1;

        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.EndElement && reader.Name is "list")
            {
                return;
            }

            if (reader.NodeType is not XmlNodeType.Element || reader.Name is not "item")
            {
                continue;
            }

            EnsureLineStart(sb);
            if (numbered)
            {
                sb.Append(index++).Append(". ");
            }
            else
            {
                sb.Append("- ");
            }

            if (!reader.IsEmptyElement)
            {
                using var subtree = reader.ReadSubtree();
                _ = subtree.Read();
                WriteNodes(subtree, sb, numbered ? ListContext.Numbered : ListContext.Bullet);
            }

            sb.Append('\n');
        }
    }

    /// <summary>
    /// Table list path.
    /// </summary>
    /// <param name="reader">Reader positioned just inside the list start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    private static void WriteListAsTable(XmlReader reader, StringBuilder sb)
    {
        var headerWritten = false;

        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.EndElement && reader.Name is "list")
            {
                return;
            }

            if (reader.NodeType is not XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.Name)
            {
                case "listheader":
                    {
                        using var subtree = reader.ReadSubtree();
                        _ = subtree.Read();
                        var (term, description) = ReadTermAndDescription(subtree);
                        sb.Append("| ").Append(term).Append(" | ").Append(description).Append(" |\n")
                            .Append("| --- | --- |\n");
                        headerWritten = true;
                        break;
                    }

                case "item":
                    {
                        if (!headerWritten)
                        {
                            sb.Append("| Term | Description |\n")
                                .Append("| --- | --- |\n");
                            headerWritten = true;
                        }

                        using var subtree = reader.ReadSubtree();
                        _ = subtree.Read();
                        var (term, description) = ReadTermAndDescription(subtree);
                        sb.Append("| ").Append(term).Append(" | ").Append(description).Append(" |\n");
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Extracts term and description from a subtree.
    /// </summary>
    /// <param name="subtree">Reader scoped to the item or header element.</param>
    /// <returns>A tuple containing the extracted term and description strings.</returns>
    private static (string Term, string Description) ReadTermAndDescription(XmlReader subtree)
    {
        var term = string.Empty;
        var description = string.Empty;

        while (subtree.Read())
        {
            if (subtree.NodeType is not XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.Name)
            {
                case "term":
                    {
                        term = TableEscape(ConvertString(subtree.ReadInnerXml()));
                        break;
                    }

                case "description":
                    {
                        description = TableEscape(ConvertString(subtree.ReadInnerXml()));
                        break;
                    }
            }
        }

        return (term.Length == 0 ? " " : term, description.Length == 0 ? " " : description);
    }

    /// <summary>
    /// Recursively writes the children of the current element.
    /// </summary>
    /// <param name="reader">Reader positioned on the parent start tag.</param>
    /// <param name="sb">Destination buffer.</param>
    /// <param name="listContext">Inherited list context.</param>
    /// <param name="suppressTags">When true, nested elements emit text only.</param>
    private static void WriteChildren(XmlReader reader, StringBuilder sb, ListContext listContext, bool suppressTags)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        using var subtree = reader.ReadSubtree();
        _ = subtree.Read();

        while (subtree.Read())
        {
            switch (subtree.NodeType)
            {
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    {
                        sb.Append(subtree.Value);
                        break;
                    }

                case XmlNodeType.Element:
                    {
                        if (suppressTags)
                        {
                            WriteChildren(subtree, sb, listContext, suppressTags: true);
                        }
                        else
                        {
                            WriteElement(subtree, sb, listContext);
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Skips past the current element's children and end tag.
    /// </summary>
    /// <param name="reader">Reader positioned on a start tag.</param>
    private static void ConsumeElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.EndElement && reader.Depth == depth)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Extracts a user-friendly short name from a Roslyn cref.
    /// </summary>
    /// <param name="cref">Roslyn cref string.</param>
    /// <returns>The extracted short name.</returns>
    private static string ShortName(string cref)
    {
        var name = cref.AsSpan();

        // Strip "T:" / "M:" / "P:" / "F:" / "E:" prefix.
        if (name.Length >= 2 && name[1] == ':')
        {
            name = name[2..];
        }

        // Strip parameter list for methods.
        var paren = name.IndexOf('(');
        if (paren >= 0)
        {
            name = name[..paren];
        }

        // Take the last identifier segment.
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        {
            name = name[(lastDot + 1)..];
        }

        // Drop the generic-arity backtick suffix.
        var backtick = name.IndexOf('`');
        if (backtick >= 0)
        {
            name = name[..backtick];
        }

        return name.ToString();
    }

    /// <summary>
    /// Ensures the buffer ends with at least one blank line.
    /// </summary>
    /// <param name="sb">Buffer to inspect.</param>
    private static void EnsureBlankLine(StringBuilder sb)
    {
        TrimTrailingWhitespace(sb);
        if (sb.Length <= 0)
        {
            return;
        }

        sb.Append("\n\n");
    }

    /// <summary>
    /// Ensures the buffer ends at a line start.
    /// </summary>
    /// <param name="sb">Buffer to inspect.</param>
    private static void EnsureLineStart(StringBuilder sb)
    {
        TrimTrailingWhitespace(sb);
        if (sb.Length <= 0)
        {
            return;
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Trims trailing whitespace from the buffer.
    /// </summary>
    /// <param name="sb">Buffer to trim.</param>
    private static void TrimTrailingWhitespace(StringBuilder sb)
    {
        while (sb.Length > 0 && char.IsWhiteSpace(sb[^1]))
        {
            sb.Length--;
        }
    }

    /// <summary>
    /// Collapses runs of internal whitespace into single spaces.
    /// </summary>
    /// <param name="sb">Buffer to compact.</param>
    /// <returns>The same StringBuilder instance with whitespace collapsed.</returns>
    private static StringBuilder CollapseWhitespace(StringBuilder sb)
    {
        var write = 0;
        var inSpaceRun = false;

        for (var read = 0; read < sb.Length; read++)
        {
            var ch = sb[read];
            if (ch is ' ' or '\t')
            {
                if (inSpaceRun)
                {
                    continue;
                }

                inSpaceRun = true;
                sb[write++] = ' ';
            }
            else
            {
                inSpaceRun = false;
                sb[write++] = ch;
            }
        }

        sb.Length = write;
        TrimTrailingWhitespace(sb);
        return sb;
    }

    /// <summary>
    /// Escapes pipes and replaces newlines with spaces for GFM tables.
    /// </summary>
    /// <param name="text">Cell content.</param>
    /// <returns>The escaped table cell content.</returns>
    private static string TableEscape(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return " ";
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '|':
                {
                    sb.Append("\\|");
                    break;
                }

                case '\n':
                case '\r':
                {
                    sb.Append(' ');
                    break;
                }

                default:
                {
                    sb.Append(ch);
                    break;
                }
            }
        }

        return sb.ToString();
    }
}
