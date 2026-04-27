// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Converts a .NET XML documentation fragment into Markdown. Two
/// overloads are provided so callers that already have an
/// <see cref="XmlReader"/> open (e.g. <see cref="DocResolver"/> walking
/// the per-symbol XML) can convert the current element's subtree
/// without round-tripping through a string — eliminating the inner
/// <c>ReadInnerXml</c> + nested <c>XmlReader.Create</c> pair that
/// dominates the doc-parse allocation profile.
/// </summary>
public interface IXmlDocToMarkdownConverter
{
    /// <summary>
    /// Converts <paramref name="xmlFragment"/> into Markdown.
    /// </summary>
    /// <param name="xmlFragment">Inner XML of one doc element.</param>
    /// <returns>Markdown-formatted doc fragment, or an empty string when nothing convertible was supplied.</returns>
    string Convert(string xmlFragment);

    /// <summary>
    /// Converts a span of inner XML directly into Markdown without
    /// going through string materialisation. Skips the XmlReader
    /// allocation entirely for plain-text spans (no inline tags); falls
    /// back to the string-based renderer for tagged content.
    /// </summary>
    /// <param name="innerXml">Inner XML span to convert.</param>
    /// <returns>Markdown-formatted doc fragment.</returns>
    string Convert(ReadOnlySpan<char> innerXml);

    /// <summary>
    /// Converts the current element's child nodes into Markdown,
    /// streaming directly from <paramref name="reader"/> without
    /// materialising an intermediate string. The reader must be
    /// positioned on a start element; on return, the reader is at the
    /// matching end element (or end-of-stream if the element was empty).
    /// </summary>
    /// <param name="reader">Reader positioned on a start element.</param>
    /// <returns>Markdown-formatted doc fragment.</returns>
    Task<string> ConvertAsync(XmlReader reader);

    /// <summary>
    /// Converts the current element's child nodes into Markdown,
    /// streaming directly from <paramref name="reader"/> without
    /// materialising an intermediate string. The reader must be
    /// positioned on a start element; on return, the reader is at the
    /// matching end element (or end-of-stream if the element was empty).
    /// </summary>
    /// <param name="reader">Reader positioned on a start element.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Markdown-formatted doc fragment.</returns>
    Task<string> ConvertAsync(XmlReader reader, CancellationToken cancellationToken);
}
