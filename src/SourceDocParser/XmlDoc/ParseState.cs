// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Internal state for the parser, using a record for immutability and easy copying.
/// </summary>
internal sealed record ParseState
{
    /// <summary>Gets the summary.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Gets the remarks.</summary>
    public string Remarks { get; init; } = string.Empty;

    /// <summary>Gets the returns.</summary>
    public string Returns { get; init; } = string.Empty;

    /// <summary>Gets the value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Gets the list of examples.</summary>
    public string[] Examples { get; init; } = [];

    /// <summary>Gets the list of parameters.</summary>
    public DocEntry[] Parameters { get; init; } = [];

    /// <summary>Gets the list of type parameters.</summary>
    public DocEntry[] TypeParameters { get; init; } = [];

    /// <summary>Gets the list of exceptions.</summary>
    public DocEntry[] Exceptions { get; init; } = [];

    /// <summary>Gets the list of seeAlso entries.</summary>
    public string[] SeeAlso { get; init; } = [];

    /// <summary>Gets a value indicating whether it has inheritdoc.</summary>
    public bool HasInheritDoc { get; init; }

    /// <summary>Gets the inheritdoc cref.</summary>
    public string? InheritDocCref { get; init; }

    /// <summary>Converts to RawDocumentation.</summary>
    /// <returns>The raw documentation.</returns>
    public RawDocumentation ToRawDocumentation() =>
        new(
            Summary: Summary,
            Remarks: Remarks,
            Returns: Returns,
            Value: Value,
            Examples: Examples,
            Parameters: Parameters,
            TypeParameters: TypeParameters,
            Exceptions: Exceptions,
            SeeAlso: SeeAlso,
            HasInheritDoc: HasInheritDoc,
            InheritDocCref: InheritDocCref);
}
