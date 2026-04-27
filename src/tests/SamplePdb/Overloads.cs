// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Overloaded methods so the walker has to disambiguate by signature.</summary>
public class Overloads
{
    /// <summary>No-arg overload.</summary>
    /// <returns>The integer 0.</returns>
    public int Run() => 0;

    /// <summary>One-arg overload.</summary>
    /// <param name="x">A value.</param>
    /// <returns>The argument.</returns>
    public int Run(int x) => x;

    /// <summary>Two-arg overload of a different shape.</summary>
    /// <param name="x">A value.</param>
    /// <param name="y">Another value.</param>
    /// <returns>Their sum.</returns>
    public int Run(int x, int y) => x + y;
}