// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// String quoter shared by <see cref="NavigationEmitter"/>'s YAML
/// and TOML emitters. Lifted out of the emitter so the (rare in
/// practice but valid in spec) escape paths — embedded
/// <c>"</c> and, for TOML, embedded <c>\</c> — can be exercised
/// directly without driving the full nav pipeline.
/// </summary>
internal static class NavigationStringQuoter
{
    /// <summary>Opening and closing quote characters added around each encoded string.</summary>
    private const int QuoteWrapperLength = 2;

    /// <summary>Characters that force a YAML scalar to be quoted.</summary>
    private static readonly SearchValues<char> _yamlReservedChars = SearchValues.Create(":#'\"[]{},&*!|>%@`");

    /// <summary>
    /// Returns either the bare scalar (when no quoting required) or
    /// a double-quoted YAML scalar with embedded quotes escaped.
    /// </summary>
    /// <param name="value">Raw scalar text.</param>
    /// <returns>The YAML-safe representation.</returns>
    public static string YamlScalar(string value) =>
        value.AsSpan().IndexOfAny(_yamlReservedChars) < 0
            ? value
            : QuoteString(value, escapeBackslashes: false);

    /// <summary>
    /// Returns the TOML double-quoted form, escaping both <c>"</c>
    /// and <c>\</c>.
    /// </summary>
    /// <param name="value">Raw string value.</param>
    /// <returns>The TOML-quoted representation.</returns>
    public static string TomlString(string value) => QuoteString(value, escapeBackslashes: true);

    /// <summary>
    /// Wraps a value in double quotes and escapes the required
    /// characters. The fast path (no escapes needed) does a single
    /// allocation via <c>string.Create</c>; the slow path also fits
    /// in one allocation by counting the required escape inserts up
    /// front.
    /// </summary>
    /// <param name="value">Raw scalar text.</param>
    /// <param name="escapeBackslashes">True to escape <c>\</c> alongside <c>"</c>.</param>
    /// <returns>The quoted, escape-safe string.</returns>
    public static string QuoteString(string value, bool escapeBackslashes)
    {
        ArgumentNullException.ThrowIfNull(value);
        var firstEscapeIndex = escapeBackslashes
            ? value.AsSpan().IndexOfAny(['\\', '"'])
            : value.IndexOf('"');
        if (firstEscapeIndex < 0)
        {
            return string.Create(
                value.Length + QuoteWrapperLength,
                value,
                static (dest, state) =>
                {
                    dest[0] = '"';
                    state.CopyTo(dest[1..]);
                    dest[^1] = '"';
                });
        }

        return string.Create(
            value.Length + QuoteWrapperLength + CountEscapes(value.AsSpan(firstEscapeIndex), escapeBackslashes),
            (Value: value, FirstEscapeIndex: firstEscapeIndex, EscapeBackslashes: escapeBackslashes),
            static (dest, state) =>
            {
                dest[0] = '"';
                state.Value.AsSpan(0, state.FirstEscapeIndex).CopyTo(dest[1..]);
                var destIndex = state.FirstEscapeIndex + 1;
                for (var i = state.FirstEscapeIndex; i < state.Value.Length; i++)
                {
                    var current = state.Value[i];
                    if (current is '"' || (state.EscapeBackslashes && current is '\\'))
                    {
                        dest[destIndex++] = '\\';
                    }

                    dest[destIndex++] = current;
                }

                dest[destIndex] = '"';
            });
    }

    /// <summary>
    /// Counts the extra escape characters needed to encode
    /// <paramref name="text"/>. Pulled out so the
    /// <c>string.Create</c> length calculation matches the inner
    /// loop's inserts byte-for-byte.
    /// </summary>
    /// <param name="text">Text to inspect.</param>
    /// <param name="escapeBackslashes">True to count <c>\</c> alongside <c>"</c>.</param>
    /// <returns>The count of inserted backslashes.</returns>
    public static int CountEscapes(in ReadOnlySpan<char> text, bool escapeBackslashes)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            count += text[i] is '"' || (escapeBackslashes && text[i] is '\\') ? 1 : 0;
        }

        return count;
    }
}
