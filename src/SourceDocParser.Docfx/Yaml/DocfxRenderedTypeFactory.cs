// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Bulk-renders an <see cref="ApiType"/>'s doc strings (and those of
/// any contained members or enum values) from raw inner XML into
/// Markdown via the supplied converter — same pattern the Zensical
/// emitter uses, kept locally so the docfx package doesn't depend on
/// Zensical-internal types.
/// </summary>
internal static class DocfxRenderedTypeFactory
{
    /// <summary>
    /// Returns a copy of <paramref name="type"/> whose
    /// <see cref="ApiType.Documentation"/> and any per-member /
    /// per-value docs have been run through <paramref name="converter"/>.
    /// </summary>
    /// <param name="type">Type with raw-XML docs (walker output).</param>
    /// <param name="converter">Converter wired with the docfx cref resolver.</param>
    /// <returns>The same type shape with Markdown-rendered docs.</returns>
    public static ApiType Render(ApiType type, XmlDocToMarkdown converter)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(converter);

        return type switch
        {
            ApiObjectType o => o with
            {
                Documentation = RenderDoc(o.Documentation, converter),
                Members = RenderMembers(o.Members, converter),
            },
            ApiUnionType u => u with
            {
                Documentation = RenderDoc(u.Documentation, converter),
                Members = RenderMembers(u.Members, converter),
            },
            ApiEnumType e => e with
            {
                Documentation = RenderDoc(e.Documentation, converter),
                Values = RenderValues(e.Values, converter),
            },
            _ => type with { Documentation = RenderDoc(type.Documentation, converter) },
        };
    }

    /// <summary>Runs every text-bearing field of <paramref name="doc"/> through <paramref name="converter"/>.</summary>
    /// <param name="doc">Raw-XML doc (walker output).</param>
    /// <param name="converter">Converter to run the fields through.</param>
    /// <returns>Markdown-rendered doc.</returns>
    private static ApiDocumentation RenderDoc(ApiDocumentation doc, XmlDocToMarkdown converter) => doc with
    {
        Summary = converter.Convert(doc.Summary),
        Remarks = converter.Convert(doc.Remarks),
        Returns = converter.Convert(doc.Returns),
        Value = converter.Convert(doc.Value),
        Examples = ConvertAll(doc.Examples, converter),
        Parameters = ConvertEntries(doc.Parameters, converter),
        TypeParameters = ConvertEntries(doc.TypeParameters, converter),
        Exceptions = ConvertEntries(doc.Exceptions, converter),
    };

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
            result[i] = members[i] with { Documentation = RenderDoc(members[i].Documentation, converter) };
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
            result[i] = values[i] with { Documentation = RenderDoc(values[i].Documentation, converter) };
        }

        return result;
    }

    /// <summary>Converts each item via <paramref name="converter"/>.</summary>
    /// <param name="fragments">Raw inner-XML fragments.</param>
    /// <param name="converter">Converter to run them through.</param>
    /// <returns>Markdown-rendered strings.</returns>
    private static string[] ConvertAll(string[] fragments, XmlDocToMarkdown converter)
    {
        if (fragments.Length is 0)
        {
            return fragments;
        }

        var result = new string[fragments.Length];
        for (var i = 0; i < fragments.Length; i++)
        {
            result[i] = converter.Convert(fragments[i]);
        }

        return result;
    }

    /// <summary>Converts each entry's value via <paramref name="converter"/>.</summary>
    /// <param name="entries">Per-key doc entries with raw inner-XML values.</param>
    /// <param name="converter">Converter to run the values through.</param>
    /// <returns>Entries with the same keys and Markdown-rendered values.</returns>
    private static DocEntry[] ConvertEntries(DocEntry[] entries, XmlDocToMarkdown converter)
    {
        if (entries.Length is 0)
        {
            return entries;
        }

        var result = new DocEntry[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            result[i] = entries[i] with { Value = converter.Convert(entries[i].Value) };
        }

        return result;
    }
}
