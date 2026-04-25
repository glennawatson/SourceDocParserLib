// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// A single documentation entry, typically a name/description pair.
/// Used for parameters, type parameters, and exceptions.
/// </summary>
/// <param name="Name">The name or cref of the entry.</param>
/// <param name="Value">The associated documentation text.</param>
public sealed record DocEntry(string Name, string Value);
