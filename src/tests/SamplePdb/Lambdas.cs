// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Lambdas — produce <c>&lt;&gt;c</c> display classes the walker should filter out.</summary>
public class Lambdas
{
    /// <summary>Returns a lambda that adds one.</summary>
    /// <returns>The lambda.</returns>
    public Func<int, int> AddOne() => static x => x + 1;
}