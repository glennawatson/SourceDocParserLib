// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// User-defined attribute carrying every TypedConstant shape the
/// walker's attribute extractor has to format: positional + named
/// arguments, a <see cref="System.Type"/> for the typeof path, an
/// enum for the enum path, and a string array for the array path.
/// Used by <see cref="AttributedTarget"/> to pin the walker's
/// AttributeExtractor end-to-end against real metadata.
/// </summary>
/// <param name="label">Positional string label captured in the constructor.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MarkerAttribute(string label) : Attribute
{
    /// <summary>Gets the positional label.</summary>
    public string Label { get; } = label;

    /// <summary>Gets or sets the named priority.</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets the named tag.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Gets or sets a type-valued named argument -- exercises the typeof formatting branch.</summary>
    public Type? TargetType { get; set; }

    /// <summary>Gets or sets an enum-valued named argument -- exercises the enum formatting branch.</summary>
    public SampleSeverity Severity { get; set; }

    /// <summary>Gets or sets an array-valued named argument -- exercises the array formatting branch.</summary>
    public string[] Tags { get; set; } = [];
}
