// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// Intermediate parser output for a single symbol's XML documentation.
/// </summary>
/// <remarks>
/// Every text-shaped property holds the raw inner XML of the
/// corresponding documentation tag, not rendered Markdown. The walker
/// no longer renders Markdown; emitters do that at render time via
/// <see cref="SourceDocParser.XmlDoc.XmlDocToMarkdown"/>.
/// </remarks>
/// <param name="Summary">Raw inner XML of the <c>&lt;summary/&gt;</c> tag.</param>
/// <param name="Remarks">Raw inner XML of the <c>&lt;remarks/&gt;</c> tag.</param>
/// <param name="Returns">Raw inner XML of the <c>&lt;returns/&gt;</c> tag.</param>
/// <param name="Value">Raw inner XML of the <c>&lt;value/&gt;</c> tag.</param>
/// <param name="Examples">Raw inner XML of each <c>&lt;example/&gt;</c> tag, in declaration order.</param>
/// <param name="Parameters">Per-parameter description; <see cref="DocEntry.Value"/> is raw inner XML.</param>
/// <param name="TypeParameters">Per-type-parameter description; <see cref="DocEntry.Value"/> is raw inner XML.</param>
/// <param name="Exceptions">Per-exception description; <see cref="DocEntry.Value"/> is raw inner XML.</param>
/// <param name="SeeAlso">Cref strings collected from top-level <c>&lt;seealso/&gt;</c> tags.</param>
/// <param name="HasInheritDoc">Whether an inheritdoc element was present.</param>
/// <param name="InheritDocCref">The inheritdoc cref attribute, if any.</param>
internal sealed record RawDocumentation(
    string Summary,
    string Remarks,
    string Returns,
    string Value,
    string[] Examples,
    DocEntry[] Parameters,
    DocEntry[] TypeParameters,
    DocEntry[] Exceptions,
    string[] SeeAlso,
    bool HasInheritDoc,
    string? InheritDocCref)
{
    /// <summary>
    /// Gets a singleton instance representing missing or empty documentation.
    /// </summary>
    public static readonly RawDocumentation Empty = new(
        Summary: string.Empty,
        Remarks: string.Empty,
        Returns: string.Empty,
        Value: string.Empty,
        Examples: [],
        Parameters: [],
        TypeParameters: [],
        Exceptions: [],
        SeeAlso: [],
        HasInheritDoc: false,
        InheritDocCref: null);

    /// <summary>
    /// Gets a value indicating whether the documentation is completely empty.
    /// </summary>
    public bool IsCompletelyEmpty =>
        Summary is []
        && Remarks is []
        && Returns is []
        && Value is []
        && Examples is []
        && Parameters is []
        && TypeParameters is []
        && Exceptions is []
        && SeeAlso is []
        && !HasInheritDoc;
}
