// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Static helpers for parsing XML attributes from a span.
/// </summary>
internal static class XmlAttributeParser
{
    /// <summary>
    /// Returns the value of <paramref name="attributeName"/> from the
    /// given <paramref name="attrArea"/>, or an empty span when not present.
    /// Only double-quoted values are supported.
    /// </summary>
    /// <param name="attrArea">The span containing attributes.</param>
    /// <param name="attributeName">Attribute name to look up.</param>
    /// <returns>The attribute value as a span, or empty.</returns>
    public static ReadOnlySpan<char> GetAttribute(ReadOnlySpan<char> attrArea, ReadOnlySpan<char> attributeName)
    {
        if (attrArea.IsEmpty)
        {
            return default;
        }

        var index = 0;
        while (index < attrArea.Length)
        {
            var attribute = ReadNextAttribute(attrArea, ref index);
            if (!attribute.IsValid)
            {
                break;
            }

            var name = attrArea.Slice(attribute.NameStart, attribute.NameLength);
            if (name.SequenceEqual(attributeName))
            {
                return attrArea.Slice(attribute.ValueStart, attribute.ValueLength);
            }
        }

        return default;
    }

    /// <summary>
    /// Reads the next attribute name/value pair from the attribute area.
    /// </summary>
    /// <param name="attrArea">The span containing attributes.</param>
    /// <param name="index">Current read position, advanced past the parsed attribute.</param>
    /// <returns>The parsed attribute result.</returns>
    internal static ParsedAttributeRange ReadNextAttribute(ReadOnlySpan<char> attrArea, ref int index)
    {
        index = SkipWhitespace(attrArea, index);
        if (index >= attrArea.Length)
        {
            return default;
        }

        var nameStart = index;
        index = AdvancePastAttributeName(attrArea, index);
        var nameLength = index - nameStart;
        index = SkipAttributeValuePrefix(attrArea, index);
        var value = ReadQuotedValue(attrArea, ref index);
        if (!value.IsValid)
        {
            return default;
        }

        return new(nameStart, nameLength, value.Start, value.Length);
    }

    /// <summary>
    /// Advances past XML whitespace characters.
    /// </summary>
    /// <param name="value">The span being scanned.</param>
    /// <param name="index">Starting index.</param>
    /// <returns>The first non-whitespace index, or the span length.</returns>
    internal static int SkipWhitespace(ReadOnlySpan<char> value, int index)
    {
        while (index < value.Length && XmlCharHelper.IsWhitespace(value[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Reads an attribute name up to whitespace or '='.
    /// </summary>
    /// <param name="attrArea">The span containing attributes.</param>
    /// <param name="index">Current read position, advanced past the name.</param>
    /// <returns>The index immediately after the attribute name.</returns>
    internal static int AdvancePastAttributeName(ReadOnlySpan<char> attrArea, int index)
    {
        while (index < attrArea.Length && attrArea[index] != '=' && !XmlCharHelper.IsWhitespace(attrArea[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Advances past the '=' separator and any surrounding whitespace.
    /// </summary>
    /// <param name="attrArea">The span containing attributes.</param>
    /// <param name="index">Starting index.</param>
    /// <returns>The index of the opening quote, if present.</returns>
    internal static int SkipAttributeValuePrefix(ReadOnlySpan<char> attrArea, int index)
    {
        while (index < attrArea.Length && (attrArea[index] == '=' || XmlCharHelper.IsWhitespace(attrArea[index])))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Reads a double-quoted attribute value.
    /// </summary>
    /// <param name="attrArea">The span containing attributes.</param>
    /// <param name="index">Current read position, advanced past the closing quote.</param>
    /// <returns>The parsed quoted value result.</returns>
    internal static ParsedSlice ReadQuotedValue(ReadOnlySpan<char> attrArea, ref int index)
    {
        if (index >= attrArea.Length || attrArea[index] != '"')
        {
            return default;
        }

        index++;
        var valueStart = index;
        var closingQuoteOffset = attrArea[index..].IndexOf('"');
        if (closingQuoteOffset < 0)
        {
            return default;
        }

        index = valueStart + closingQuoteOffset + 1;
        return new(valueStart, closingQuoteOffset);
    }
}
