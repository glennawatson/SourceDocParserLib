// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Adapts <see cref="ApiDocumentation"/>, whose text fields hold raw
/// inner XML as of v0.3, into a same-shaped <see cref="ApiDocumentation"/>
/// whose fields hold rendered Markdown. Run once per page render so
/// the existing emitter code can keep consuming the model directly.
/// </summary>
internal static class ApiDocumentationExtensions
{
    /// <summary>
    /// Returns a copy of <paramref name="doc"/> with every text-bearing
    /// field run through <paramref name="converter"/>. Cref-bearing
    /// fields (<see cref="ApiDocumentation.SeeAlso"/>) and the
    /// inheritance marker stay untouched — those are UID strings and a
    /// display name, not doc content.
    /// </summary>
    /// <param name="doc">Doc with raw inner-XML fragments (walker output).</param>
    /// <param name="converter">Converter wired with the emitter's <see cref="ICrefResolver"/>.</param>
    /// <returns>The same doc shape with Markdown-rendered text fields.</returns>
    public static ApiDocumentation RenderWith(this ApiDocumentation doc, XmlDocToMarkdown converter)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(converter);

        return doc with
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
    }

    /// <summary>Converts each item in <paramref name="fragments"/> via <paramref name="converter"/>.</summary>
    /// <param name="fragments">Raw inner-XML fragments.</param>
    /// <param name="converter">Converter to run them through.</param>
    /// <returns>Markdown-rendered strings, one per input item.</returns>
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

    /// <summary>Converts each entry's <see cref="DocEntry.Value"/> via <paramref name="converter"/>; keys pass through untouched.</summary>
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
