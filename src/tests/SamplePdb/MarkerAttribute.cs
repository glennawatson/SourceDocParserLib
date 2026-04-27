// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// User-defined attribute carrying both a positional argument and
/// two named arguments — used by <see cref="AttributedTarget"/> to
/// pin the walker's attribute-extraction path against a usage with
/// arguments (the SamplePdb anchor only carries no-arg and BCL
/// attributes via the source-link toolchain).
/// </summary>
/// <param name="label">Positional label captured in the constructor.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MarkerAttribute(string label) : Attribute
{
    /// <summary>Gets the positional label.</summary>
    public string Label { get; } = label;

    /// <summary>Gets or sets the named priority.</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets the named tag.</summary>
    public string Tag { get; set; } = string.Empty;
}
