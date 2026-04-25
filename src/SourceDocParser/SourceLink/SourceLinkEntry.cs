// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// A pair of symbol UID and source URL collected during extraction.
/// </summary>
/// <param name="Uid">Roslyn documentation member ID.</param>
/// <param name="Url">Resolved blob URL with line anchor.</param>
public readonly record struct SourceLinkEntry(string Uid, string Url);
