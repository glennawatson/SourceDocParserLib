// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Parsed XML documentation for a single symbol.
/// </summary>
/// <param name="Summary">The summary text.</param>
/// <param name="Remarks">The remarks text.</param>
/// <param name="Returns">The returns text for methods.</param>
/// <param name="Value">The value text for properties.</param>
/// <param name="Examples">Markdown example blocks.</param>
/// <param name="Parameters">Parameter name to description mapping.</param>
/// <param name="TypeParameters">Type parameter name to description mapping.</param>
/// <param name="Exceptions">Exception type to description mapping.</param>
/// <param name="SeeAlso">Related symbol UIDs.</param>
/// <param name="InheritedFrom">Name of the symbol this documentation was inherited from.</param>
public sealed record ApiDocumentation(
    string Summary,
    string Remarks,
    string Returns,
    string Value,
    List<string> Examples,
    List<DocEntry> Parameters,
    List<DocEntry> TypeParameters,
    List<DocEntry> Exceptions,
    List<string> SeeAlso,
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
