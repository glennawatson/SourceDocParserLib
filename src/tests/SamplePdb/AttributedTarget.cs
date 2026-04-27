// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// Carries a <see cref="MarkerAttribute"/> usage with a positional
/// argument plus two named arguments so the walker's
/// <c>AttributeExtractor</c> path is pinned end-to-end on real
/// metadata (not just a synthetic <c>ApiAttribute</c>).
/// </summary>
[Marker("primary", Priority = 7, Tag = "fixture")]
public class AttributedTarget;
