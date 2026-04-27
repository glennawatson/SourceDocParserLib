// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Helper class for parsing XML documentation fragments.
/// </summary>
internal static class DocXmlParser
{
    /// <summary>
    /// Parses one member XML fragment into a RawDocumentation. Driven
    /// by DocXmlScanner — a span-based forward scanner — instead of
    /// XmlReader, so the per-symbol parse no longer allocates an
    /// XmlTextReaderImpl with its multi-KB buffers and the inner
    /// element bodies are surfaced as ReadOnlySpan slices rather than
    /// fresh strings.
    /// </summary>
    /// <param name="memberXml">Raw member XML.</param>
    /// <param name="context">Per-resolver state bundle.</param>
    /// <returns>The parsed raw documentation.</returns>
    public static RawDocumentation Parse(string memberXml, DocResolveContext context)
    {
        var state = new ParseState();
        var scanner = new DocXmlScanner(memberXml.AsSpan());
        var converter = context.Converter;

        while (scanner.Read())
        {
            if (scanner.Kind == DocTokenKind.StartElement)
            {
                state = HandleElement(ref scanner, converter, state);
            }
        }

        return state.ToRawDocumentation();
    }

    /// <summary>
    /// Handles a start element by dispatching to the appropriate tag handler.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    internal static ParseState HandleElement(ref DocXmlScanner scanner, IXmlDocToMarkdownConverter converter, ParseState state)
    {
        if (IsCommonTag(scanner.Name))
        {
            return HandleCommonTag(ref scanner, converter, state);
        }

        if (scanner.Name is "example")
        {
            return state with { Examples = [.. state.Examples, converter.Convert(scanner.ReadInnerSpan())] };
        }

        if (scanner.Name is "param")
        {
            return HandleParam(ref scanner, converter, state);
        }

        return HandleNonCommonElement(ref scanner, converter, state);
    }

    /// <summary>
    /// Returns true when the tag is one of the common single-value documentation tags.
    /// </summary>
    /// <param name="elementName">Element name to test.</param>
    /// <returns>True when the tag is handled by <see cref="HandleCommonTag"/>.</returns>
    internal static bool IsCommonTag(ReadOnlySpan<char> elementName) =>
        elementName is "summary" or "remarks" or "returns" or "value";

    /// <summary>
    /// Handles the non-common documentation tags.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    internal static ParseState HandleNonCommonElement(ref DocXmlScanner scanner, IXmlDocToMarkdownConverter converter, ParseState state) =>
        scanner.Name switch
        {
            "typeparam" => HandleTypeParam(ref scanner, converter, state),
            "exception" => HandleException(ref scanner, converter, state),
            "seealso" => HandleSeeAlso(ref scanner, state),
            "inheritdoc" => HandleInheritDoc(ref scanner, state),
            _ => state,
        };

    /// <summary>
    /// Handles common documentation tags like summary, remarks, returns, and value.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleCommonTag(ref DocXmlScanner scanner, IXmlDocToMarkdownConverter converter, ParseState state) =>
        scanner.Name switch
        {
            "summary" => state with { Summary = converter.Convert(scanner.ReadInnerSpan()) },
            "remarks" => state with { Remarks = converter.Convert(scanner.ReadInnerSpan()) },
            "returns" => state with { Returns = converter.Convert(scanner.ReadInnerSpan()) },
            "value" => state with { Value = converter.Convert(scanner.ReadInnerSpan()) },
            _ => state
        };

    /// <summary>
    /// Handles the <c>param</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleParam(ref DocXmlScanner scanner, IXmlDocToMarkdownConverter converter, ParseState state) =>
        scanner.GetAttribute("name") is [_, ..] paramName
            ? state with
            {
                Parameters =
                [.. state.Parameters, new(paramName.ToString(), converter.Convert(scanner.ReadInnerSpan()))]
            }
            : state;

    /// <summary>
    /// Handles the <c>typeparam</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleTypeParam(ref DocXmlScanner scanner, IXmlDocToMarkdownConverter converter, ParseState state) =>
        scanner.GetAttribute("name") is [_, ..] typeParamName
            ? state with
            {
                TypeParameters =
                [.. state.TypeParameters, new(typeParamName.ToString(), converter.Convert(scanner.ReadInnerSpan()))]
            }
            : state;

    /// <summary>
    /// Handles the <c>exception</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleException(ref DocXmlScanner scanner, IXmlDocToMarkdownConverter converter, ParseState state) =>
        scanner.GetAttribute("cref") is [_, ..] exceptionCref
            ? state with
            {
                Exceptions =
                [.. state.Exceptions, new(exceptionCref.ToString(), converter.Convert(scanner.ReadInnerSpan()))]
            }
            : state;

    /// <summary>
    /// Handles the <c>seealso</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleSeeAlso(ref DocXmlScanner scanner, ParseState state) =>
        scanner.GetAttribute("cref") is [_, ..] seeAlsoCref
            ? state with { SeeAlso = [.. state.SeeAlso, seeAlsoCref.ToString()] }
            : state;

    /// <summary>
    /// Handles the <c>inheritdoc</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleInheritDoc(ref DocXmlScanner scanner, ParseState state)
    {
        var inheritCref = scanner.GetAttribute("cref");

        // <inheritdoc>…</inheritdoc> with content: skip the
        // body rather than processing it (rarely used and not
        // part of any standard).
        scanner.SkipElement();

        return state with
        {
            HasInheritDoc = true,
            InheritDocCref = inheritCref is [_, ..] ? inheritCref.ToString() : state.InheritDocCref
        };
    }
}
