// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Static class hosting a classic extension method.</summary>
public static class Extensions
{
    /// <summary>Multiplier used to double the extension receiver.</summary>
    private const int Multiplier = 2;

    /// <summary>Doubles the receiver.</summary>
    /// <param name="self">The receiver.</param>
    /// <returns>Twice the input.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "SST1703", Justification = "The fixture tests classic extension-method metadata separately from extension blocks.")]
    public static int Doubled(this int self) => self * Multiplier;
}
