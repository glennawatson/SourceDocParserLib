// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SamplePdb;

/// <summary>Plain instance method, no parameters.</summary>
public class PlainInstance
{
    /// <summary>Returns a fixed string.</summary>
    /// <returns>A literal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Hello() => "hello";
}
