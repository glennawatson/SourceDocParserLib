// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>The other concrete case of the <see cref="SampleShape"/> union.</summary>
/// <param name="Side">The square's side length.</param>
public sealed record SampleSquare(double Side) : SampleShape;
