// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using SourceDocParser.Common;

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
            return MemberDisplayFormatter.FormatWithParens(containingType.Name, ParameterTypeNames(member.Parameters));
        }

        var memberSegment = TakesParens(member.Kind)
            ? MemberDisplayFormatter.FormatWithParens(member.Name, ParameterTypeNames(member.Parameters))
            : member.Name;
        return MemberDisplayFormatter.Concat(containingType.Name, '.', memberSegment);
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
    /// Materialises the parameter type display names into a transient
    /// <c>string[]</c> the Common formatter consumes.
    /// </summary>
    /// <param name="parameters">Member parameter list.</param>
    /// <returns>The display-name array.</returns>
    private static string[] ParameterTypeNames(ApiParameter[] parameters)
    {
        if (parameters is [])
        {
            return [];
        }

        var names = new string[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            names[i] = parameters[i].Type.DisplayName;
        }

        return names;
    }
}
