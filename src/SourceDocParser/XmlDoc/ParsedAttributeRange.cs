// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Parsed attribute name/value locations.
/// </summary>
internal readonly record struct ParsedAttributeRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ParsedAttributeRange"/> struct.
    /// </summary>
    /// <param name="nameStart">Start offset of the attribute name.</param>
    /// <param name="nameLength">Length of the attribute name.</param>
    /// <param name="valueStart">Start offset of the attribute value.</param>
    /// <param name="valueLength">Length of the attribute value.</param>
    public ParsedAttributeRange(int nameStart, int nameLength, int valueStart, int valueLength)
    {
        NameStart = nameStart;
        NameLength = nameLength;
        ValueStart = valueStart;
        ValueLength = valueLength;
        IsValid = true;
    }

    /// <summary>
    /// Gets the start offset of the attribute name.
    /// </summary>
    public int NameStart { get; }

    /// <summary>
    /// Gets the length of the attribute name.
    /// </summary>
    public int NameLength { get; }

    /// <summary>
    /// Gets the start offset of the attribute value.
    /// </summary>
    public int ValueStart { get; }

    /// <summary>
    /// Gets the length of the attribute value.
    /// </summary>
    public int ValueLength { get; }

    /// <summary>
    /// Gets a value indicating whether the parse succeeded.
    /// </summary>
    public bool IsValid { get; }
}
