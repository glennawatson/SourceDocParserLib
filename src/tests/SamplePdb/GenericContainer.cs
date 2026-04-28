// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace SamplePdb;

/// <summary>Generic class with a generic method -- exercises type-parameter symbols.</summary>
/// <typeparam name="T">Element type.</typeparam>
public class GenericContainer<T>
{
    /// <summary>Boxes <paramref name="value"/> into a single-element array.</summary>
    /// <typeparam name="TOther">A second type parameter on the method.</typeparam>
    /// <param name="value">Value to wrap.</param>
    /// <param name="value2">The second value.</param>
    /// <returns>The wrapped array.</returns>
    public T[] Wrap<TOther>([DisallowNull] T value, [DisallowNull] TOther value2)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value2 == null)
        {
            throw new ArgumentNullException(nameof(value2));
        }

        return [value];
    }
}
