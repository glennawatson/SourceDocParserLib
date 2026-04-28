// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser;

/// <summary>
/// Bulk-renders an <see cref="ApiType"/>'s doc strings (and those of
/// any contained members or enum values) from raw inner XML into
/// Markdown via the supplied converter. Shared by the Zensical and
/// docfx emitters -- both fold the doc-render pass over each type
/// before the existing Markdown-shaped renderer consumes the result,
/// so this helper sits in the core library to avoid duplicating the
/// switch-on-derivation logic per emitter.
/// </summary>
public static class RenderedTypeFactory
{
    /// <summary>
    /// Returns a copy of <paramref name="type"/> whose
    /// <see cref="ApiType.Documentation"/> and any per-member /
    /// per-value docs have been run through <paramref name="converter"/>.
    /// </summary>
    /// <param name="type">Type with raw-XML docs (walker output).</param>
    /// <param name="converter">Converter wired with the emitter's <see cref="ICrefResolver"/>.</param>
    /// <returns>The same type shape with Markdown-rendered docs.</returns>
    public static ApiType Render(ApiType type, XmlDocToMarkdown converter)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(converter);

        var renderedDoc = type.Documentation.RenderWith(converter);

        // Returning the input type unchanged when nothing rendered
        // saves the per-type record allocation -- dominant once the
        // member-array short-circuit kicks in for compiler-generated
        // accessors and BCL-shaped inherits.
        return type switch
        {
            ApiObjectType o => RebuildObject(o, renderedDoc, RenderMembers(o.Members, converter)),
            ApiUnionType u => RebuildUnion(u, renderedDoc, RenderMembers(u.Members, converter)),
            ApiEnumType e => RebuildEnum(e, renderedDoc, RenderValues(e.Values, converter)),
            _ => ReferenceEquals(renderedDoc, type.Documentation) ? type : type with { Documentation = renderedDoc },
        };
    }

    /// <summary>
    /// Returns a copy of <paramref name="member"/> whose docs have
    /// been rendered. Member-page emitters render one member at a
    /// time and call this to fold conversion into the existing flow.
    /// </summary>
    /// <param name="member">Member with raw-XML docs.</param>
    /// <param name="converter">Converter wired with the emitter's <see cref="ICrefResolver"/>.</param>
    /// <returns>A member with Markdown-rendered docs.</returns>
    public static ApiMember Render(ApiMember member, XmlDocToMarkdown converter)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(converter);
        var rendered = member.Documentation.RenderWith(converter);
        return ReferenceEquals(rendered, member.Documentation) ? member : member with { Documentation = rendered };
    }

    /// <summary>
    /// Renders every member's docs. Returns the input array unchanged
    /// when none of the per-member <see cref="ApiDocumentation"/>
    /// records actually transformed -- the common case for types whose
    /// members are all undocumented (overrides, accessor pairs).
    /// </summary>
    /// <param name="members">Members to render.</param>
    /// <param name="converter">Converter to run them through.</param>
    /// <returns>A new array when at least one member changed, otherwise the input array.</returns>
    private static ApiMember[] RenderMembers(ApiMember[] members, XmlDocToMarkdown converter)
    {
        if (members.Length is 0)
        {
            return members;
        }

        ApiMember[]? result = null;
        for (var i = 0; i < members.Length; i++)
        {
            var rendered = Render(members[i], converter);
            if (result is null)
            {
                if (ReferenceEquals(rendered, members[i]))
                {
                    continue;
                }

                result = new ApiMember[members.Length];
                Array.Copy(members, result, i);
            }

            result[i] = rendered;
        }

        return result ?? members;
    }

    /// <summary>
    /// Renders every enum value's docs. Same skip-on-no-change shape
    /// as <see cref="RenderMembers"/> so undocumented enums never
    /// pay the array allocation.
    /// </summary>
    /// <param name="values">Values to render.</param>
    /// <param name="converter">Converter to run them through.</param>
    /// <returns>A new array when at least one value changed, otherwise the input array.</returns>
    private static ApiEnumValue[] RenderValues(ApiEnumValue[] values, XmlDocToMarkdown converter)
    {
        if (values.Length is 0)
        {
            return values;
        }

        ApiEnumValue[]? result = null;
        for (var i = 0; i < values.Length; i++)
        {
            var renderedDoc = values[i].Documentation.RenderWith(converter);
            if (result is null)
            {
                if (ReferenceEquals(renderedDoc, values[i].Documentation))
                {
                    continue;
                }

                result = new ApiEnumValue[values.Length];
                Array.Copy(values, result, i);
            }

            result[i] = ReferenceEquals(renderedDoc, values[i].Documentation)
                ? values[i]
                : values[i] with { Documentation = renderedDoc };
        }

        return result ?? values;
    }

    /// <summary>Builds an <see cref="ApiObjectType"/> wrapper, returning the input when nothing changed so the parent type-array short-circuit can kick in.</summary>
    /// <param name="type">Original type.</param>
    /// <param name="renderedDoc">Already-rendered documentation.</param>
    /// <param name="renderedMembers">Already-rendered members (may be reference-equal to the input).</param>
    /// <returns>A type with rendered fields, or the original on no-op.</returns>
    private static ApiObjectType RebuildObject(ApiObjectType type, ApiDocumentation renderedDoc, ApiMember[] renderedMembers) =>
        ReferenceEquals(renderedDoc, type.Documentation) && ReferenceEquals(renderedMembers, type.Members)
            ? type
            : type with { Documentation = renderedDoc, Members = renderedMembers };

    /// <summary>Builds an <see cref="ApiUnionType"/> wrapper, returning the input when nothing changed.</summary>
    /// <param name="type">Original type.</param>
    /// <param name="renderedDoc">Already-rendered documentation.</param>
    /// <param name="renderedMembers">Already-rendered members (may be reference-equal to the input).</param>
    /// <returns>A type with rendered fields, or the original on no-op.</returns>
    private static ApiUnionType RebuildUnion(ApiUnionType type, ApiDocumentation renderedDoc, ApiMember[] renderedMembers) =>
        ReferenceEquals(renderedDoc, type.Documentation) && ReferenceEquals(renderedMembers, type.Members)
            ? type
            : type with { Documentation = renderedDoc, Members = renderedMembers };

    /// <summary>Builds an <see cref="ApiEnumType"/> wrapper, returning the input when nothing changed.</summary>
    /// <param name="type">Original type.</param>
    /// <param name="renderedDoc">Already-rendered documentation.</param>
    /// <param name="renderedValues">Already-rendered values (may be reference-equal to the input).</param>
    /// <returns>A type with rendered fields, or the original on no-op.</returns>
    private static ApiEnumType RebuildEnum(ApiEnumType type, ApiDocumentation renderedDoc, ApiEnumValue[] renderedValues) =>
        ReferenceEquals(renderedDoc, type.Documentation) && ReferenceEquals(renderedValues, type.Values)
            ? type
            : type with { Documentation = renderedDoc, Values = renderedValues };
}
