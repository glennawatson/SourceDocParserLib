// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Location metadata for the next entity candidate.
/// </summary>
internal readonly record struct EntityMatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityMatch"/> struct.
    /// </summary>
    /// <param name="entityStart">Index of the ampersand.</param>
    public EntityMatch(int entityStart)
    {
        EntityStart = entityStart;
        HasAmpersand = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityMatch"/> struct.
    /// </summary>
    /// <param name="entityStart">Index of the ampersand.</param>
    /// <param name="semicolonIndex">Index of the terminating semicolon.</param>
    public EntityMatch(int entityStart, int semicolonIndex)
    {
        EntityStart = entityStart;
        SemicolonIndex = semicolonIndex;
        HasAmpersand = true;
        HasSemicolon = true;
    }

    /// <summary>
    /// Gets a value indicating whether an ampersand was found.
    /// </summary>
    public bool HasAmpersand { get; }

    /// <summary>
    /// Gets a value indicating whether a terminating semicolon was found.
    /// </summary>
    public bool HasSemicolon { get; }

    /// <summary>
    /// Gets the index of the ampersand that begins the entity.
    /// </summary>
    public int EntityStart { get; }

    /// <summary>
    /// Gets the index of the terminating semicolon.
    /// </summary>
    public int SemicolonIndex { get; }
}
