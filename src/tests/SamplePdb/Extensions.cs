// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Static class hosting a classic extension method.</summary>
public static class Extensions
{
    private const int Multiplier = 2;

    /// <summary>Doubles the receiver.</summary>
    /// <param name="self">The value to double.</param>
    /// <returns>Twice the input.</returns>
    public static int Doubled(this int self) => self * Multiplier;
}
