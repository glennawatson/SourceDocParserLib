// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Outer type containing a nested type — exercises nested-type uid generation.</summary>
public class Outer
{
    /// <summary>Returns a fresh nested instance.</summary>
    /// <returns>A new <see cref="Nested"/>.</returns>
    public Nested Make() => new();

    /// <summary>Nested public type.</summary>
    public class Nested
    {
        /// <summary>A trivial method on the nested type.</summary>
        /// <returns>True, always.</returns>
        public bool IsNested() => true;
    }
}