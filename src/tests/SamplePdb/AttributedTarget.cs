// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// Carries a <see cref="MarkerAttribute"/> usage with a positional
/// string + named arguments covering every TypedConstant shape:
/// primitive (int / string), <c>Type</c> via <c>typeof</c>, an enum
/// value, and a string array. Pins the AttributeExtractor's
/// FormatConstant switch arms end-to-end on real metadata.
/// </summary>
[Marker(
    "primary",
    Priority = 7,
    Tag = "fixture",
    TargetType = typeof(SampleShape),
    Severity = SampleSeverity.Warning,
    Tags = ["alpha", "beta"])]
public class AttributedTarget;
