// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Standard properties + indexer.</summary>
public class WithProperties
{
    /// <summary>Gets or sets a string property.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets a read-only computed property.</summary>
    public int Length => Name.Length;

    /// <summary>Indexer with a single int parameter.</summary>
    /// <param name="index">The position.</param>
    /// <returns>The character at that position.</returns>
    public char this[int index] => Name[index];
}