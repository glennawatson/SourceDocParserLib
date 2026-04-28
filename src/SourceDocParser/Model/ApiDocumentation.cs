// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// Parsed XML documentation for a single symbol.
/// </summary>
/// <remarks>
/// Every text-shaped property carries the <strong>raw inner XML</strong>
/// of the corresponding documentation tag. Emitters convert each
/// fragment at render time via
/// <see cref="SourceDocParser.XmlDoc.XmlDocToMarkdown"/> with their
/// own <see cref="ICrefResolver"/> deciding how
/// <c>see cref="..."/</c> references resolve in the target output.
/// The walker stays oblivious to the eventual Markdown shape so each
/// emitter picks its own link-resolution strategy.
/// </remarks>
/// <param name="Summary">Raw inner XML of the <c>summary/</c> tag.</param>
/// <param name="Remarks">Raw inner XML of the <c>remarks/</c> tag.</param>
/// <param name="Returns">Raw inner XML of the <c>returns/</c> tag (methods).</param>
/// <param name="Value">Raw inner XML of the <c>value/</c> tag (properties).</param>
/// <param name="Examples">Raw inner XML of each <c>example/</c> tag, in declaration order.</param>
/// <param name="Parameters">
/// Per-parameter description list. <see cref="DocEntry.Name"/> is the
/// parameter name; <see cref="DocEntry.Value"/> the raw inner XML of
/// the <c>param/</c> tag.
/// </param>
/// <param name="TypeParameters">
/// Per-type-parameter description list. <see cref="DocEntry.Name"/>
/// is the type parameter name; <see cref="DocEntry.Value"/> the raw
/// inner XML of the <c>typeparam/</c> tag.
/// </param>
/// <param name="Exceptions">
/// Per-exception description list. <see cref="DocEntry.Name"/> is
/// the cref of the exception type; <see cref="DocEntry.Value"/> the
/// raw inner XML of the <c>exception/</c> tag.
/// </param>
/// <param name="SeeAlso">Cref strings collected from top-level <c>seealso/</c> tags. These are unrendered UIDs -- the emitter formats them via its resolver.</param>
/// <param name="InheritedFrom">Display name of the symbol whose documentation was auto- or explicitly inherited, when applicable.</param>
public sealed record ApiDocumentation(
    string Summary,
    string Remarks,
    string Returns,
    string Value,
    string[] Examples,
    DocEntry[] Parameters,
    DocEntry[] TypeParameters,
    DocEntry[] Exceptions,
    string[] SeeAlso,
    string? InheritedFrom)
{
    /// <summary>
    /// Gets a singleton instance representing missing or empty documentation.
    /// </summary>
    public static readonly ApiDocumentation Empty = new(
        Summary: string.Empty,
        Remarks: string.Empty,
        Returns: string.Empty,
        Value: string.Empty,
        Examples: [],
        Parameters: [],
        TypeParameters: [],
        Exceptions: [],
        SeeAlso: [],
        InheritedFrom: null);

    /// <summary>
    /// Gets a value indicating whether the documentation is completely empty.
    /// </summary>
    public bool IsEmpty => this == Empty;
}
