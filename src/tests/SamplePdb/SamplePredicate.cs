// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// Generic delegate — exercises arity + type-parameter capture on a
/// delegate (the walker has to thread <see cref="System.Type"/>-style
/// generic args through a <c>TypeKind.Delegate</c> as well as classes).
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
/// <param name="value">The element to inspect.</param>
/// <returns>True when accepted.</returns>
public delegate bool SamplePredicate<T>(T value);
