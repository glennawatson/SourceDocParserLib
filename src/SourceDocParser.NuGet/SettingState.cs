// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet;

/// <summary>
/// Tri-state describing what a single <c>nuget.config</c> file
/// declared for a per-key setting — drives the discovery walk's
/// continue / stop logic. Mirrors NuGet's effective merge
/// semantics: a closer file's <c>&lt;clear/&gt;</c> wipes any
/// less-specific parent's value.
/// </summary>
public enum SettingState
{
    /// <summary>Config didn't mention the setting; caller keeps walking the chain.</summary>
    NotMentioned,

    /// <summary>Config carries a <c>&lt;clear /&gt;</c> in the relevant section; caller stops walking and falls back to the platform default.</summary>
    Cleared,

    /// <summary>Config explicitly set the value; caller uses <see cref="ConfigSettingResult.Value"/>.</summary>
    Found,
}
