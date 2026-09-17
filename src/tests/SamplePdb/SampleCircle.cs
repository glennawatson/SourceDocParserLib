// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>One concrete case of the <see cref="SampleShape"/> union.</summary>
/// <param name="Radius">The circle's radius.</param>
[System.Diagnostics.DebuggerDisplay("SampleCircle: {ToString(),nq}")]
public sealed record SampleCircle(double Radius) : SampleShape;
