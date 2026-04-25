// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// A mapping entry from a SourceLink JSON documents collection.
/// </summary>
/// <param name="LocalPrefix">The local path prefix (or full path if not a wildcard).</param>
/// <param name="UrlPrefix">The remote URL prefix (or full URL if not a wildcard).</param>
/// <param name="IsWildcard">True if the entry ends with a wildcard.</param>
internal readonly record struct SourceLinkMapEntry(string LocalPrefix, string UrlPrefix, bool IsWildcard);
