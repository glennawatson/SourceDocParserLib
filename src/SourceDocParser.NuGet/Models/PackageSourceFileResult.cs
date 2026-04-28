// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Models;

/// <summary>
/// Per-file view of a nuget.config <c>packageSources</c>
/// section -- used by the discovery walk to layer cross-file
/// merge semantics. <see cref="ClearedSeen"/> tells the walk
/// whether this file's <c>clear/</c> should wipe the
/// less-specific (parent) sources it accumulated; <see cref="Sources"/>
/// is the post-clear ordered list of <c>add</c> entries.
/// </summary>
/// <param name="ClearedSeen">True when the file invoked <c>clear/</c> at least once inside the section.</param>
/// <param name="Sources">Ordered, deduplicated entries declared in this file (post-clear if any).</param>
public readonly record struct PackageSourceFileResult(bool ClearedSeen, PackageSource[] Sources);
