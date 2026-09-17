// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>The other concrete case of the <see cref="SampleShape"/> union.</summary>
/// <param name="Side">The square's side length.</param>
[System.Diagnostics.DebuggerDisplay("SampleSquare: {ToString(),nq}")]
public sealed record SampleSquare(double Side) : SampleShape;
