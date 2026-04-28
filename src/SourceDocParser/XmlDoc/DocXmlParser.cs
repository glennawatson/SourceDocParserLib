// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Parses one member-level XML doc fragment into a structured
/// <see cref="RawDocumentation"/>. The returned strings are the
/// <strong>raw inner XML</strong> of each documentation tag -- they
/// are <em>not</em> Markdown. Emitters convert each fragment via
/// <see cref="XmlDocToMarkdown"/> at render time, with their own
/// <see cref="ICrefResolver"/> deciding how cross-references resolve
/// in the target output. Capturing raw XML at parse time keeps the
/// walker oblivious to the eventual Markdown shape and lets each
/// emitter adopt its own cref-resolution strategy.
/// </summary>
internal static class DocXmlParser
{
    /// <summary>
    /// Parses one member XML fragment into a
    /// <see cref="RawDocumentation"/>. Driven by
    /// <see cref="DocXmlScanner"/> -- a span-based forward scanner --
    /// instead of <c>XmlReader</c>, so the per-symbol parse no longer
    /// allocates an <c>XmlTextReaderImpl</c> with its multi-KB buffers
    /// and the inner element bodies are surfaced as raw inner-XML
    /// strings rather than rendered Markdown.
    /// </summary>
    /// <param name="memberXml">Raw member XML.</param>
    /// <param name="context">Per-resolver state bundle.</param>
    /// <returns>The parsed raw documentation (every text field holds inner-XML, not Markdown).</returns>
    public static RawDocumentation Parse(string memberXml, DocResolveContext context)
    {
        var state = new ParseState();
        var scanner = new DocXmlScanner(memberXml.AsSpan());

        while (scanner.Read())
        {
            if (scanner.Kind == DocTokenKind.StartElement)
            {
                state = HandleElement(ref scanner, state);
            }
        }

        return state.ToRawDocumentation();
    }

    /// <summary>
    /// Handles a start element by dispatching to the appropriate tag handler.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    internal static ParseState HandleElement(ref DocXmlScanner scanner, ParseState state)
    {
        if (IsCommonTag(scanner.Name))
        {
            return HandleCommonTag(ref scanner, state);
        }

        if (scanner.Name is "example")
        {
            return state with { Examples = [.. state.Examples, scanner.ReadInnerSpan().ToString()] };
        }

        if (scanner.Name is "param")
        {
            return HandleParam(ref scanner, state);
        }

        return HandleNonCommonElement(ref scanner, state);
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
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    internal static ParseState HandleNonCommonElement(ref DocXmlScanner scanner, ParseState state) =>
        scanner.Name switch
        {
            "typeparam" => HandleTypeParam(ref scanner, state),
            "exception" => HandleException(ref scanner, state),
            "seealso" => HandleSeeAlso(ref scanner, state),
            "inheritdoc" => HandleInheritDoc(ref scanner, state),
            _ => state,
        };

    /// <summary>
    /// Handles common documentation tags like summary, remarks, returns, and value.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleCommonTag(ref DocXmlScanner scanner, ParseState state) =>
        scanner.Name switch
        {
            "summary" => state with { Summary = scanner.ReadInnerSpan().ToString() },
            "remarks" => state with { Remarks = scanner.ReadInnerSpan().ToString() },
            "returns" => state with { Returns = scanner.ReadInnerSpan().ToString() },
            "value" => state with { Value = scanner.ReadInnerSpan().ToString() },
            _ => state
        };

    /// <summary>
    /// Handles the <c>param</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleParam(ref DocXmlScanner scanner, ParseState state) =>
        scanner.GetAttribute("name") is [_, ..] paramName
            ? state with
            {
                Parameters =
                [.. state.Parameters, new(paramName.ToString(), scanner.ReadInnerSpan().ToString())]
            }
            : state;

    /// <summary>
    /// Handles the <c>typeparam</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleTypeParam(ref DocXmlScanner scanner, ParseState state) =>
        scanner.GetAttribute("name") is [_, ..] typeParamName
            ? state with
            {
                TypeParameters =
                [.. state.TypeParameters, new(typeParamName.ToString(), scanner.ReadInnerSpan().ToString())]
            }
            : state;

    /// <summary>
    /// Handles the <c>exception</c> tag.
    /// </summary>
    /// <param name="scanner">The scanner.</param>
    /// <param name="state">The parse state.</param>
    /// <returns>The updated parse state.</returns>
    private static ParseState HandleException(ref DocXmlScanner scanner, ParseState state) =>
        scanner.GetAttribute("cref") is [_, ..] exceptionCref
            ? state with
            {
                Exceptions =
                [.. state.Exceptions, new(exceptionCref.ToString(), scanner.ReadInnerSpan().ToString())]
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

        // <inheritdoc>...</inheritdoc> with content: skip the
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
