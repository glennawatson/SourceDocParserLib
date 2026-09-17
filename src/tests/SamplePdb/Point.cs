// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Record struct primary constructor.</summary>
/// <param name="X">First coord.</param>
/// <param name="Y">Second coord.</param>
[System.Diagnostics.DebuggerDisplay("Point: {ToString(),nq}")]
public readonly record struct Point(int X, int Y);
