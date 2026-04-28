// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Operator overload -- exercises the operator-method walker path.</summary>
public record WithOperator
{
    /// <summary>Gets the wrapped value.</summary>
    public int Value { get; init; }

    /// <summary>Adds two instances together.</summary>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>A new instance whose value is the sum.</returns>
    public static WithOperator operator +(WithOperator left, WithOperator right) => Add(left, right);

    /// <summary>
    /// Adds the value of another instance to this one.
    /// </summary>
    /// <param name="left">Left.</param>
    /// <param name="right">Right.</param>
    /// <returns>A new instance whose value is the sum.</returns>
    public static WithOperator Add(WithOperator left, WithOperator right) => new() { Value = left.Value + right.Value };
}
