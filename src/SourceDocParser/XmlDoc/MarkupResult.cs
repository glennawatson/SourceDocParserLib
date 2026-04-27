// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Result of a markup scan.
/// </summary>
internal readonly ref struct MarkupResult
{
    /// <summary>Gets the kind of token produced.</summary>
    public DocTokenKind Kind { get; init; }

    /// <summary>Gets the new position in the input.</summary>
    public int NewPos { get; init; }

    /// <summary>Gets the element name (for start/end elements).</summary>
    public ReadOnlySpan<char> Name { get; init; }

    /// <summary>Gets the raw text (for CDATA).</summary>
    public ReadOnlySpan<char> RawText { get; init; }

    /// <summary>Gets the attribute area (for start elements).</summary>
    public ReadOnlySpan<char> AttrArea { get; init; }

    /// <summary>Gets a value indicating whether it is an empty element.</summary>
    public bool IsEmptyElement { get; init; }

    /// <summary>Gets a value indicating whether the input was truncated or invalid.</summary>
    public bool Success { get; init; }

    /// <summary>Gets a value indicating whether the token should be consumed silently (comments, PI).</summary>
    public bool IsSilent { get; init; }
}
