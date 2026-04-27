// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common;

/// <summary>
/// Pre-sized, single-allocation builders for the friendly member-name
/// strings both emitter packages need: <c>Foo()</c>, <c>Foo(int, string)</c>,
/// <c>Type.Method(args)</c>. The hot path uses
/// <see cref="string.Create{TState}"/> with a precomputed length so each
/// rendered name allocates exactly one string and nothing else.
/// Stays free of the parser model so Common keeps its tight, no-deps
/// surface — callers pass in the raw label and parameter type display
/// names array.
/// </summary>
public static class MemberDisplayFormatter
{
    /// <summary>Single comma-space separator — matches both the docfx and Microsoft Learn conventions.</summary>
    private const string ParameterSeparator = ", ";

    /// <summary>
    /// Builds <paramref name="label"/> + <c>(arg1, arg2)</c>. Empty
    /// <paramref name="parameterTypeDisplayNames"/> short-circuits to
    /// <c>label + "()"</c>; non-empty path computes the exact length and
    /// fills a single span.
    /// </summary>
    /// <param name="label">Name root (member or type name).</param>
    /// <param name="parameterTypeDisplayNames">Parameter type display names, in declaration order.</param>
    /// <returns>The fully formatted name, e.g. <c>Run(int, string)</c>.</returns>
    public static string FormatWithParens(string label, string[] parameterTypeDisplayNames)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(parameterTypeDisplayNames);
        if (parameterTypeDisplayNames is [])
        {
            return string.Create(label.Length + 2, label, static (span, source) =>
            {
                source.AsSpan().CopyTo(span);
                span[^2] = '(';
                span[^1] = ')';
            });
        }

        var totalLength = ComputeFormattedLength(label, parameterTypeDisplayNames);
        return string.Create(totalLength, (label, parameterTypeDisplayNames), static (span, state) =>
        {
            var (lbl, parms) = state;
            lbl.AsSpan().CopyTo(span);
            var cursor = lbl.Length;
            span[cursor++] = '(';
            for (var i = 0; i < parms.Length; i++)
            {
                if (i > 0)
                {
                    ParameterSeparator.AsSpan().CopyTo(span[cursor..]);
                    cursor += ParameterSeparator.Length;
                }

                var typeName = parms[i];
                typeName.AsSpan().CopyTo(span[cursor..]);
                cursor += typeName.Length;
            }

            span[cursor] = ')';
        });
    }

    /// <summary>
    /// Joins three string parts with a single separator character via
    /// <see cref="string.Create{TState}"/>. Returns <paramref name="suffix"/>
    /// untouched when <paramref name="prefix"/> is empty so callers can
    /// safely qualify into a global namespace.
    /// </summary>
    /// <param name="prefix">Left part.</param>
    /// <param name="separator">Single character to insert between the parts.</param>
    /// <param name="suffix">Right part.</param>
    /// <returns>The concatenated string.</returns>
    public static string Concat(string prefix, char separator, string suffix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        if (prefix is [])
        {
            return suffix;
        }

        return string.Create(prefix.Length + 1 + suffix.Length, (prefix, separator, suffix), static (span, state) =>
        {
            state.prefix.AsSpan().CopyTo(span);
            span[state.prefix.Length] = state.separator;
            state.suffix.AsSpan().CopyTo(span[(state.prefix.Length + 1)..]);
        });
    }

    /// <summary>Sums the final character count of the formatted name.</summary>
    /// <param name="label">Name root.</param>
    /// <param name="parameterTypeDisplayNames">Non-empty parameter type display names.</param>
    /// <returns>Total character count including parens and separators.</returns>
    private static int ComputeFormattedLength(string label, string[] parameterTypeDisplayNames)
    {
        var total = label.Length + 2;
        for (var i = 0; i < parameterTypeDisplayNames.Length; i++)
        {
            if (i > 0)
            {
                total += ParameterSeparator.Length;
            }

            total += parameterTypeDisplayNames[i].Length;
        }

        return total;
    }
}
