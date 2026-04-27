// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// Intermediate parser output for a single symbol's XML documentation.
/// </summary>
/// <param name="Summary">The raw summary text.</param>
/// <param name="Remarks">The raw remarks text.</param>
/// <param name="Returns">The raw returns text.</param>
/// <param name="Value">The raw value text.</param>
/// <param name="Examples">Markdown example blocks.</param>
/// <param name="Parameters">Parameter name to description mapping.</param>
/// <param name="TypeParameters">Type parameter name to description mapping.</param>
/// <param name="Exceptions">Exception type to description mapping.</param>
/// <param name="SeeAlso">Related symbol UIDs.</param>
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
