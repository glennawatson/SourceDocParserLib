// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>A single documentation entry, typically a name/description pair. Used for parameters, type parameters, and exceptions.</summary>
/// <param name="Name">The name (parameter / type parameter) or cref (exception) of the entry.</param>
/// <param name="Value">Raw inner XML of the documentation tag.</param>
/// <remarks>
/// <see cref="Value"/> carries the raw inner XML of the source
/// documentation tag; emitters convert it at render time via
/// <see cref="XmlDoc.XmlDocToMarkdown"/>.
/// </remarks>
[System.Diagnostics.DebuggerDisplay("DocEntry: {ToString(),nq}")]
public sealed record DocEntry(string Name, string Value);
