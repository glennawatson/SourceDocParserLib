// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Static helpers for parsing XML markup (tags, comments, CDATA, PI).
/// </summary>
internal static class XmlMarkupParser
{
    /// <summary>Opening delimiter for an XML comment.</summary>
    private const string CommentOpen = "<!--";

    /// <summary>Closing delimiter for an XML comment.</summary>
    private const string CommentClose = "-->";

    /// <summary>Opening delimiter for a CDATA section.</summary>
    private const string CdataOpen = "<![CDATA[";

    /// <summary>Closing delimiter for a CDATA section.</summary>
    private const string CdataClose = "]]>";

    /// <summary>Closing delimiter for a processing instruction.</summary>
    private const string PiClose = "?>";

    /// <summary>
    /// Length of the opening delimiter for an XML tag.
    /// </summary>
    private const int LtLen = 1;

    /// <summary>
    /// Length of the prefix for an end element tag.
    /// </summary>
    private const int EndElementPrefixLen = 2;

    /// <summary>
    /// Consumes a markup token starting at the current opening character.
    /// Dispatches on the character immediately after <c>&lt;</c> so the
    /// common-case start element doesn't pay for four upstream
    /// <c>TryRead</c> probes -- a measurable hit on the per-symbol
    /// XmlDocToMarkdown bench.
    /// </summary>
    /// <param name="input">The full input span.</param>
    /// <param name="pos">The current position.</param>
    /// <returns>A <see cref="MarkupResult"/> describing what was found.</returns>
    public static MarkupResult ReadMarkup(ReadOnlySpan<char> input, int pos)
    {
        if (pos + LtLen >= input.Length)
        {
            return ReadStartElement(input, pos);
        }

        switch (input[pos + LtLen])
        {
            case '!':
                {
                    // <!-- comment --> or <![CDATA[ ... ]]>; both start with '<!'.
                    if (TryReadComment(input, pos, out var commentResult))
                    {
                        return commentResult;
                    }

                    if (TryReadCdata(input, pos, out var cdataResult))
                    {
                        return cdataResult;
                    }

                    return ReadStartElement(input, pos);
                }

            case '?':
                {
                    TryReadProcessingInstruction(input, pos, out var piResult);
                    return piResult;
                }

            case '/':
                {
                    TryReadEndElement(input, pos, out var endElementResult);
                    return endElementResult;
                }

            default:
                {
                    return ReadStartElement(input, pos);
                }
        }
    }

    /// <summary>
    /// Tries to read an XML comment.
    /// </summary>
    /// <param name="input">The input span.</param>
    /// <param name="pos">The current position.</param>
    /// <param name="result">The result if successful.</param>
    /// <returns>Whether a comment was found.</returns>
    internal static bool TryReadComment(ReadOnlySpan<char> input, int pos, out MarkupResult result)
    {
        if (pos + CommentOpen.Length <= input.Length && input[pos..(pos + CommentOpen.Length)].SequenceEqual(CommentOpen))
        {
            var afterOpen = pos + CommentOpen.Length;
            var end = input[afterOpen..].IndexOf(CommentClose, StringComparison.Ordinal);
            if (end < 0)
            {
                result = new MarkupResult { NewPos = input.Length, Kind = DocTokenKind.None, Success = false };
                return true;
            }

            result = new MarkupResult { NewPos = afterOpen + end + CommentClose.Length, IsSilent = true, Success = true };
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Tries to read a CDATA section.
    /// </summary>
    /// <param name="input">The input span.</param>
    /// <param name="pos">The current position.</param>
    /// <param name="result">The result if successful.</param>
    /// <returns>Whether a CDATA section was found.</returns>
    internal static bool TryReadCdata(ReadOnlySpan<char> input, int pos, out MarkupResult result)
    {
        if (pos + CdataOpen.Length <= input.Length && input[pos..(pos + CdataOpen.Length)].SequenceEqual(CdataOpen))
        {
            var afterOpen = pos + CdataOpen.Length;
            var end = input[afterOpen..].IndexOf(CdataClose, StringComparison.Ordinal);
            if (end < 0)
            {
                result = new MarkupResult { NewPos = input.Length, Kind = DocTokenKind.None, Success = false };
                return true;
            }

            result = new MarkupResult
            {
                RawText = input[afterOpen..(afterOpen + end)],
                NewPos = afterOpen + end + CdataClose.Length,
                Kind = DocTokenKind.Text,
                Success = true
            };
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Tries to read a processing instruction.
    /// </summary>
    /// <param name="input">The input span.</param>
    /// <param name="pos">The current position.</param>
    /// <param name="result">The result if successful.</param>
    /// <returns>Whether a processing instruction was found.</returns>
    internal static bool TryReadProcessingInstruction(ReadOnlySpan<char> input, int pos, out MarkupResult result)
    {
        if (pos + LtLen + 1 <= input.Length && input[pos + LtLen] == '?')
        {
            var afterOpen = pos + LtLen + 1;
            var end = input[afterOpen..].IndexOf(PiClose, StringComparison.Ordinal);
            if (end < 0)
            {
                result = new MarkupResult { NewPos = input.Length, Kind = DocTokenKind.None, Success = false };
                return true;
            }

            result = new MarkupResult { NewPos = afterOpen + end + PiClose.Length, IsSilent = true, Success = true };
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Tries to read an end element.
    /// </summary>
    /// <param name="input">The input span.</param>
    /// <param name="pos">The current position.</param>
    /// <param name="result">The result if successful.</param>
    /// <returns>Whether an end element was found.</returns>
    internal static bool TryReadEndElement(ReadOnlySpan<char> input, int pos, out MarkupResult result)
    {
        if (pos + EndElementPrefixLen <= input.Length && input[pos + LtLen] == '/')
        {
            var nameStart = pos + EndElementPrefixLen;
            var end = input[nameStart..].IndexOf('>');
            if (end < 0)
            {
                result = new MarkupResult { NewPos = input.Length, Kind = DocTokenKind.None, Success = false };
                return true;
            }

            result = new MarkupResult
            {
                Name = input[nameStart..(nameStart + end)].Trim(),
                NewPos = nameStart + end + 1,
                Kind = DocTokenKind.EndElement,
                Success = true
            };
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Reads a start element.
    /// </summary>
    /// <param name="input">The input span.</param>
    /// <param name="pos">The current position.</param>
    /// <returns>A <see cref="MarkupResult"/> describing what was found.</returns>
    internal static MarkupResult ReadStartElement(ReadOnlySpan<char> input, int pos)
    {
        var tagEnd = input[pos..].IndexOf('>');
        if (tagEnd < 0)
        {
            return new MarkupResult { NewPos = input.Length, Kind = DocTokenKind.None, Success = false };
        }

        var tagBody = input[(pos + LtLen)..(pos + tagEnd)];
        var isEmptyElement = tagBody is [.., '/'];
        if (isEmptyElement)
        {
            tagBody = tagBody[..^1];
        }

        // Split element name vs attribute area at first whitespace.
        var nameEnd = 0;
        while (nameEnd < tagBody.Length && !XmlCharHelper.IsWhitespace(tagBody[nameEnd]))
        {
            nameEnd++;
        }

        return new MarkupResult
        {
            Name = tagBody[..nameEnd],
            AttrArea = tagBody[nameEnd..],
            NewPos = pos + tagEnd + 1,
            Kind = DocTokenKind.StartElement,
            IsEmptyElement = isEmptyElement,
            Success = true
        };
    }
}
