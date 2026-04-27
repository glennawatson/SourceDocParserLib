// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Parsed span location.
/// </summary>
internal readonly record struct ParsedSlice
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ParsedSlice"/> struct.
    /// </summary>
    /// <param name="start">Start offset of the parsed span.</param>
    /// <param name="length">Length of the parsed span.</param>
    public ParsedSlice(int start, int length)
    {
        Start = start;
        Length = length;
        IsValid = true;
    }

    /// <summary>
    /// Gets the start offset of the parsed span.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the length of the parsed span.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets a value indicating whether the parse succeeded.
    /// </summary>
    public bool IsValid { get; }
}