// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Static helpers for parsing XML attributes from a span.
/// </summary>
internal static class XmlAttributeParser
{
    /// <summary>The four whitespace characters allowed inside an XML start tag.</summary>
    private static readonly SearchValues<char> WhitespaceChars = SearchValues.Create(" \t\r\n");

    /// <summary>Whitespace plus <c>=</c> -- anything that ends an attribute name or precedes the opening quote.</summary>
    private static readonly SearchValues<char> NameTerminatorChars = SearchValues.Create("= \t\r\n");

    /// <summary>
    /// Returns the value of <paramref name="attributeName"/> from the
    /// given <paramref name="attrArea"/>, or an empty span when not
    /// present. Only double-quoted values are supported. The lookup
    /// runs once per attribute on every
    /// <see cref="DocXmlScanner.GetAttribute"/> call, so the parse is
    /// kept to a single inner method -- chaining the per-step helpers
    /// (whitespace skip, name read, equals skip, quoted value read)
    /// shows up directly in <c>XmlDocToMarkdownBenchmarks</c>.
    /// </summary>
    /// <param name="attrArea">The span containing attributes.</param>
    /// <param name="attributeName">Attribute name to look up.</param>
    /// <returns>The attribute value as a span, or empty.</returns>
    public static ReadOnlySpan<char> GetAttribute(ReadOnlySpan<char> attrArea, ReadOnlySpan<char> attributeName)
    {
        var index = 0;
        while (TryReadAttributeInline(attrArea, ref index, out var nameStart, out var nameLength, out var valueStart, out var valueLength))
        {
            if (attrArea.Slice(nameStart, nameLength).SequenceEqual(attributeName))
            {
                return attrArea.Slice(valueStart, valueLength);
            }
        }

        return default;
    }

    /// <summary>
    /// Reads one <c>name="value"</c> pair from <paramref name="attrArea"/>,
    /// advancing <paramref name="index"/> past it. Uses
    /// <see cref="MemoryExtensions.IndexOfAnyExcept{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
    /// for the whitespace / name / equals scans so the per-attribute
    /// loops stay vectorised -- the per-symbol render calls this on
    /// every doc tag and the cumulative cost shows up in
    /// <c>XmlDocToMarkdownBenchmarks</c>.
    /// </summary>
    /// <param name="attrArea">The attribute area to scan.</param>
    /// <param name="index">Read cursor; advanced past the parsed pair.</param>
    /// <param name="nameStart">Start offset of the attribute name on success.</param>
    /// <param name="nameLength">Length of the attribute name on success.</param>
    /// <param name="valueStart">Start offset of the attribute value on success.</param>
    /// <param name="valueLength">Length of the attribute value on success.</param>
    /// <returns>True when an attribute was parsed; false on end-of-area or malformed input.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadAttributeInline(
        ReadOnlySpan<char> attrArea,
        ref int index,
        out int nameStart,
        out int nameLength,
        out int valueStart,
        out int valueLength)
    {
        nameStart = 0;
        nameLength = 0;
        valueStart = 0;
        valueLength = 0;

        // Skip leading whitespace.
        var nonWsOffset = attrArea[index..].IndexOfAnyExcept(WhitespaceChars);
        if (nonWsOffset < 0)
        {
            return false;
        }

        index += nonWsOffset;

        // Capture the attribute name up to '=' or whitespace.
        nameStart = index;
        var nameEndOffset = attrArea[index..].IndexOfAny(NameTerminatorChars);
        var nameEnd = nameEndOffset < 0 ? attrArea.Length : index + nameEndOffset;
        nameLength = nameEnd - index;
        index = nameEnd;

        // Skip past '=' and any surrounding whitespace.
        var quoteOffset = attrArea[index..].IndexOfAnyExcept(NameTerminatorChars);
        if (quoteOffset < 0)
        {
            return false;
        }

        index += quoteOffset;

        // Only double-quoted values are supported.
        if (attrArea[index] != '"')
        {
            return false;
        }

        index++;
        valueStart = index;
        var closingQuoteOffset = attrArea[index..].IndexOf('"');
        if (closingQuoteOffset < 0)
        {
            return false;
        }

        valueLength = closingQuoteOffset;
        index = valueStart + closingQuoteOffset + 1;
        return true;
    }
}
