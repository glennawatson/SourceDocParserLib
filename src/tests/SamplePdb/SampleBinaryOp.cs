// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// Delegate with two parameters and a return value — exercises the
/// walker's <c>TypeKind.Delegate</c> branch and delegate-signature
/// capture (parameter list, return type).
/// </summary>
/// <param name="left">First operand.</param>
/// <param name="right">Second operand.</param>
/// <returns>An aggregate of the inputs.</returns>
public delegate int SampleBinaryOp(int left, int right);
