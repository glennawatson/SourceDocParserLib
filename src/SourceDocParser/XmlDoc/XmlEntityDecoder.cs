// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Text;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// XML entity-reference decoder. Handles the five standard entities
/// (<c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;amp;</c>, <c>&amp;quot;</c>,
/// <c>&amp;apos;</c>) plus numeric character references in decimal
/// (<c>&amp;#42;</c>) or hex (<c>&amp;#x2A;</c>) form. Lifted out of
/// <see cref="DocXmlScanner"/> so the entity rules read at problem-
/// domain level and can be unit-tested without a scanner instance —
/// the scanner is a <c>ref struct</c>, so anything that lives on it
/// is awkward to construct from a test fixture.
/// </summary>
internal static class XmlEntityDecoder
{
    /// <summary>Standard XML entity name for the less-than sign.</summary>
    private const string EntityLt = "lt";

    /// <summary>Standard XML entity name for the greater-than sign.</summary>
    private const string EntityGt = "gt";

    /// <summary>Standard XML entity name for the ampersand.</summary>
    private const string EntityAmp = "amp";

    /// <summary>Standard XML entity name for the double-quote.</summary>
    private const string EntityQuot = "quot";

    /// <summary>Standard XML entity name for the apostrophe.</summary>
    private const string EntityApos = "apos";

    /// <summary>
    /// Decodes the five standard entities + numeric character
    /// references into <paramref name="dest"/>. Unknown entities are
    /// silently dropped (matches XmlReader's behaviour for undefined
    /// entities); a malformed entity (no closing semicolon) is
    /// appended verbatim.
    /// </summary>
    /// <param name="dest">Destination buffer (appended to).</param>
    /// <param name="text">Raw text slice (entity-encoded).</param>
    public static void AppendDecoded(StringBuilder dest, in ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(dest);
        var i = 0;
        while (i < text.Length)
        {
            var amp = text[i..].IndexOf('&');
            if (amp < 0)
            {
                dest.Append(text[i..]);
                return;
            }

            dest.Append(text[i..(i + amp)]);
            var entityStart = i + amp;
            var semi = text[entityStart..].IndexOf(';');
            if (semi < 0)
            {
                // Malformed; append the rest verbatim.
                dest.Append(text[entityStart..]);
                return;
            }

            var entity = text[(entityStart + 1)..(entityStart + semi)];
            switch (entity)
            {
                case EntityLt:
                    {
                        dest.Append('<');
                        break;
                    }

                case EntityGt:
                    {
                        dest.Append('>');
                        break;
                    }

                case EntityAmp:
                    {
                        dest.Append('&');
                        break;
                    }

                case EntityQuot:
                    {
                        dest.Append('"');
                        break;
                    }

                case EntityApos:
                    {
                        dest.Append('\'');
                        break;
                    }

                default:
                    {
                        if (entity.Length > 1 && entity[0] == '#' && TryParseNumericRef(entity[1..], out var rune))
                        {
                            dest.Append(rune);
                        }

                        break;
                    }
            }

            // Unknown entity → silently dropped.
            i = entityStart + semi + 1;
        }
    }

    /// <summary>
    /// Parses a numeric character reference body — decimal digits or
    /// the hex form (leading <c>x</c> / <c>X</c> prefix). The leading
    /// hash sign is stripped by the caller. Code points outside the
    /// BMP (above 0xFFFF) are rejected; the scanner never produces
    /// them in practice and the rest of the pipeline is char-based.
    /// </summary>
    /// <param name="body">Numeric reference body.</param>
    /// <param name="rune">Decoded code point.</param>
    /// <returns>True when the reference parsed.</returns>
    public static bool TryParseNumericRef(in ReadOnlySpan<char> body, out char rune)
    {
        rune = '\0';
        if (body is [])
        {
            return false;
        }

        int code;
        if (body[0] is 'x' or 'X')
        {
            if (!int.TryParse(body[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
            {
                return false;
            }
        }
        else if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
        {
            return false;
        }

        if (code is < 0 or > 0xFFFF)
        {
            return false;
        }

        rune = (char)code;
        return true;
    }
}
