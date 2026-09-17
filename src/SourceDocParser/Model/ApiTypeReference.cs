// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>A reference to a type.</summary>
/// <param name="DisplayName">The human-readable name.</param>
/// <param name="Uid">The documentation member ID.</param>
[System.Diagnostics.DebuggerDisplay("ApiTypeReference: {ToString(),nq}")]
public sealed record ApiTypeReference(string DisplayName, string Uid);
