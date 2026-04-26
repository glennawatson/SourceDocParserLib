// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SourceDocParser.Zensical;

/// <summary>
/// Builds the friendly heading text used by
/// <see cref="MemberPageEmitter"/> — the Microsoft Learn convention
/// where constructors render as <c>TypeName(args)</c>, methods and
/// operators include their parameter list, and properties / fields
/// / events show their bare name. Mirrors the logic in
/// <c>SourceDocParser.Docfx.DocfxMemberDisplayName</c>; duplicated
/// per emitter so the markdown package stays free of any docfx
/// dependency.
/// </summary>
internal static class ZensicalMemberDisplayName
{
    /// <summary>Single comma-space separator — matches mkdocs-Material header convention.</summary>
    private const string ParameterSeparator = ", ";

    /// <summary>
    /// Returns the heading text for the overload group page —
    /// <c>TypeName(args)</c> for constructors, <c>Type.Method(args)</c>
    /// for methods, <c>Type.Property</c> for properties.
    /// </summary>
    /// <param name="member">First overload in the group.</param>
    /// <param name="containingType">Declaring type.</param>
    /// <returns>The heading text, ready to follow the leading <c>#</c>.</returns>
    public static string Heading(ApiMember member, ApiType containingType)
    {
        if (member.Kind == ApiMemberKind.Constructor)
        {
            return FormatWithParens(containingType.Name, member.Parameters);
        }

        var memberSegment = TakesParens(member.Kind)
            ? FormatWithParens(member.Name, member.Parameters)
            : member.Name;
        return Concat(containingType.Name, '.', memberSegment);
    }

    /// <summary>
    /// Tests whether a member kind takes a parameter list when rendered as
    /// a Markdown heading — true for constructors, methods, and operators.
    /// </summary>
    /// <param name="kind">Member kind.</param>
    /// <returns>True when the friendly heading ends in <c>(args)</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TakesParens(ApiMemberKind kind) => kind switch
    {
        ApiMemberKind.Constructor => true,
        ApiMemberKind.Method => true,
        ApiMemberKind.Operator => true,
        _ => false,
    };

    /// <summary>
    /// Builds <paramref name="label"/> + <c>(arg1, arg2)</c>. Zero-param
    /// fast path returns <c>label + "()"</c> via <see cref="string.Create{TState}"/>.
    /// Multi-param path computes the exact final length, allocates once,
    /// and fills the span with no intermediate strings.
    /// </summary>
    /// <param name="label">Name root (member or type name).</param>
    /// <param name="parameters">Member parameter list.</param>
    /// <returns>The fully-formatted name.</returns>
    internal static string FormatWithParens(string label, ApiParameter[] parameters)
    {
        if (parameters is [])
        {
            return string.Create(label.Length + 2, label, static (span, source) =>
            {
                source.AsSpan().CopyTo(span);
                span[^2] = '(';
                span[^1] = ')';
            });
        }

        var totalLength = ComputeFormattedLength(label, parameters);
        return string.Create(totalLength, (label, parameters), static (span, state) =>
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

                var typeName = parms[i].Type.DisplayName;
                typeName.AsSpan().CopyTo(span[cursor..]);
                cursor += typeName.Length;
            }

            span[cursor] = ')';
        });
    }

    /// <summary>
    /// Sums the final character count of the formatted name so the
    /// hot-path <see cref="string.Create{TState}"/> can allocate
    /// exactly the right span size up front.
    /// </summary>
    /// <param name="label">Name root.</param>
    /// <param name="parameters">Member parameter list (non-empty).</param>
    /// <returns>Total character count including the surrounding parens and separators.</returns>
    private static int ComputeFormattedLength(string label, ApiParameter[] parameters)
    {
        var total = label.Length + 2;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                total += ParameterSeparator.Length;
            }

            total += parameters[i].Type.DisplayName.Length;
        }

        return total;
    }

    /// <summary>
    /// Joins three string parts with a single separator character via
    /// <see cref="string.Create{TState}"/> — zero intermediate allocations.
    /// </summary>
    /// <param name="prefix">Left part.</param>
    /// <param name="separator">Single character to insert between the parts.</param>
    /// <param name="suffix">Right part.</param>
    /// <returns>The concatenated string.</returns>
    private static string Concat(string prefix, char separator, string suffix) =>
        prefix is []
            ? suffix
            : string.Create(prefix.Length + 1 + suffix.Length, (prefix, separator, suffix), static (span, state) =>
            {
                state.prefix.AsSpan().CopyTo(span);
                span[state.prefix.Length] = state.separator;
                state.suffix.AsSpan().CopyTo(span[(state.prefix.Length + 1)..]);
            });
}
