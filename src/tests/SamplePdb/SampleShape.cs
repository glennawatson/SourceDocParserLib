// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SamplePdb;

/// <summary>
/// Closed-hierarchy union base -- implements the
/// <see cref="IUnion"/> marker the walker keys its <c>IsUnion</c>
/// probe off. The case classes <see cref="SampleCircle"/> and
/// <see cref="SampleSquare"/> derive directly from this base.
/// </summary>
public abstract record SampleShape : IUnion;
