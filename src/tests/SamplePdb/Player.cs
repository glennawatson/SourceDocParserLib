// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Record with a primary constructor -- checks positional-record handling.</summary>
/// <param name="Name">The name.</param>
/// <param name="Score">The score.</param>
[System.Diagnostics.DebuggerDisplay("Player: {ToString(),nq}")]
public sealed record Player(string Name, int Score);
