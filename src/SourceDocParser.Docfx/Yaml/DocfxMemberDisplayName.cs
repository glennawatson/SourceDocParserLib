// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SourceDocParser.Docfx;

/// <summary>
/// Builds the <c>name</c> / <c>nameWithType</c> / <c>fullName</c>
/// strings docfx writes on each member item — friendly forms like
/// <c>ReactiveObject()</c> for constructors and
/// <c>Method(int, string)</c> for methods, instead of raw metadata
/// names like <c>.ctor</c> or unqualified method names. Hot path
/// uses <see cref="string.Create{TState}"/> with a precomputed
/// length so each rendered name allocates one string and nothing
/// else; zero-parameter members short-circuit to the bare name and
/// allocate nothing beyond the underlying name reference.
/// </summary>
internal static class DocfxMemberDisplayName
{
    /// <summary>Single comma-space separator — matches docfx's <c>, </c> output.</summary>
    private const string ParameterSeparator = ", ";

    /// <summary>
    /// Returns the unqualified friendly name for <paramref name="member"/>:
    /// <c>TypeName(arg, arg)</c> for constructors, <c>MethodName(arg, arg)</c>
    /// for methods/operators, the bare name for properties, fields, events.
    /// </summary>
    /// <param name="member">Member to render.</param>
    /// <param name="containingType">Declaring type — supplies the constructor display name.</param>
    /// <returns>Allocated string ready to drop into the YAML scalar position.</returns>
    public static string Unqualified(ApiMember member, ApiType containingType)
    {
        var label = LabelFor(member, containingType);
        return TakesParens(member.Kind)
            ? FormatWithParens(label, member.Parameters)
            : label;
    }

    /// <summary>
    /// Returns <c>TypeName.MemberFriendlyName</c> — used for the
    /// <c>nameWithType</c> field. Reuses <see cref="Unqualified"/>
    /// internally then prepends the type-name dot prefix.
    /// </summary>
    /// <param name="member">Member to render.</param>
    /// <param name="containingType">Declaring type.</param>
    /// <returns>Qualified friendly name.</returns>
    public static string Qualified(ApiMember member, ApiType containingType) =>
        Concat(containingType.Name, '.', Unqualified(member, containingType));

    /// <summary>
    /// Returns <c>Namespace.TypeName.MemberFriendlyName</c> — used for the
    /// <c>fullName</c> field. Falls back to <see cref="Qualified"/>
    /// when the type has no namespace (global namespace).
    /// </summary>
    /// <param name="member">Member to render.</param>
    /// <param name="containingType">Declaring type.</param>
    /// <returns>Fully-qualified friendly name.</returns>
    public static string FullyQualified(ApiMember member, ApiType containingType) =>
        Concat(containingType.FullName, '.', Unqualified(member, containingType));

    /// <summary>
    /// Returns the docfx <c>overload:</c> anchor — <c>member.Uid + "*"</c>.
    /// One allocation per call via <see cref="string.Create{TState}"/>.
    /// </summary>
    /// <param name="memberUid">The member's documentation comment ID.</param>
    /// <returns>The anchor string.</returns>
    public static string OverloadAnchor(string memberUid) =>
        string.Create(memberUid.Length + 1, memberUid, static (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[^1] = '*';
        });

    /// <summary>
    /// Tests whether a member kind takes a parameter list when rendered as
    /// a docfx <c>name</c> — true for constructors, methods, and operators.
    /// </summary>
    /// <param name="kind">Member kind.</param>
    /// <returns>True when the friendly form ends in <c>(args)</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TakesParens(ApiMemberKind kind) => kind switch
    {
        ApiMemberKind.Constructor => true,
        ApiMemberKind.Method => true,
        ApiMemberKind.Operator => true,
        _ => false,
    };

    /// <summary>
    /// Picks the visible name root: the containing type's simple name
    /// for constructors (so <c>.ctor</c> renders as the type name), the
    /// member's own name otherwise.
    /// </summary>
    /// <param name="member">Member to label.</param>
    /// <param name="containingType">Declaring type — supplies the ctor label.</param>
    /// <returns>The unqualified label root, before any parameter list.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string LabelFor(ApiMember member, ApiType containingType) =>
        member.Kind == ApiMemberKind.Constructor ? containingType.Name : member.Name;

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
            // Fast path: label + "()"; one alloc, no separator work.
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
    /// Used by <see cref="Qualified"/> and <see cref="FullyQualified"/>.
    /// </summary>
    /// <param name="prefix">Left part.</param>
    /// <param name="separator">Single character to insert between the parts.</param>
    /// <param name="suffix">Right part.</param>
    /// <returns>The concatenated string.</returns>
    private static string Concat(string prefix, char separator, string suffix)
    {
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
}
