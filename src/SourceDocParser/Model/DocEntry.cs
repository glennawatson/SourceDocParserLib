// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// A single documentation entry, typically a name/description pair.
/// Used for parameters, type parameters, and exceptions.
/// </summary>
/// <remarks>
/// As of v0.3 <see cref="Value"/> carries the raw inner XML of the
/// source documentation tag, not pre-rendered Markdown. Emitters
/// convert it at render time via
/// <see cref="SourceDocParser.XmlDoc.XmlDocToMarkdown"/>.
/// </remarks>
/// <param name="Name">The name (parameter / type parameter) or cref (exception) of the entry.</param>
/// <param name="Value">Raw inner XML of the documentation tag.</param>
public sealed record DocEntry(string Name, string Value);
