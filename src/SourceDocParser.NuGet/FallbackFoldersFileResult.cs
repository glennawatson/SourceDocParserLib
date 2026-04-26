// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet;

/// <summary>
/// Per-file view of a nuget.config <c>&lt;fallbackPackageFolders&gt;</c>
/// section — used by the discovery walk to honour list-section
/// semantics (<c>&lt;clear/&gt;</c> wipes parents).
/// </summary>
/// <param name="ClearedSeen">True when the file invoked <c>&lt;clear/&gt;</c> at least once inside the section.</param>
/// <param name="Folders">Ordered, deduplicated folder paths declared in this file (post-clear if any).</param>
public readonly record struct FallbackFoldersFileResult(bool ClearedSeen, string[] Folders);
