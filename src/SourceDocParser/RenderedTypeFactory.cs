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
/// docfx emitters — both fold the doc-render pass over each type
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

        return type switch
        {
            ApiObjectType o => o with
            {
                Documentation = o.Documentation.RenderWith(converter),
                Members = RenderMembers(o.Members, converter),
            },
            ApiUnionType u => u with
            {
                Documentation = u.Documentation.RenderWith(converter),
                Members = RenderMembers(u.Members, converter),
            },
            ApiEnumType e => e with
            {
                Documentation = e.Documentation.RenderWith(converter),
                Values = RenderValues(e.Values, converter),
            },
            _ => type with { Documentation = type.Documentation.RenderWith(converter) },
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
        return member with { Documentation = member.Documentation.RenderWith(converter) };
    }

    /// <summary>Renders every member's docs.</summary>
    /// <param name="members">Members to render.</param>
    /// <param name="converter">Converter to run them through.</param>
    /// <returns>A new array with rendered-doc members.</returns>
    private static ApiMember[] RenderMembers(ApiMember[] members, XmlDocToMarkdown converter)
    {
        if (members.Length is 0)
        {
            return members;
        }

        var result = new ApiMember[members.Length];
        for (var i = 0; i < members.Length; i++)
        {
            result[i] = Render(members[i], converter);
        }

        return result;
    }

    /// <summary>Renders every enum value's docs.</summary>
    /// <param name="values">Values to render.</param>
    /// <param name="converter">Converter to run them through.</param>
    /// <returns>A new array with rendered-doc values.</returns>
    private static ApiEnumValue[] RenderValues(ApiEnumValue[] values, XmlDocToMarkdown converter)
    {
        if (values.Length is 0)
        {
            return values;
        }

        var result = new ApiEnumValue[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = values[i] with { Documentation = values[i].Documentation.RenderWith(converter) };
        }

        return result;
    }
}
