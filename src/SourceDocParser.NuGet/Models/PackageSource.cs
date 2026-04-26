// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet;

/// <summary>
/// One <c>&lt;add&gt;</c> entry from a nuget.config
/// <c>&lt;packageSources&gt;</c> section. <see cref="Key"/> is the
/// human-readable name (<c>nuget.org</c>, <c>github</c>, …) and
/// <see cref="Url"/> is the V2/V3 service-index URL the fetcher
/// hits to discover and download packages.
/// </summary>
/// <param name="Key">Friendly source name; case-sensitive identity for clear/upsert merges.</param>
/// <param name="Url">Service-index or feed URL (typically the V3 <c>index.json</c> for nuget.org / GitHub Packages).</param>
public readonly record struct PackageSource(string Key, string Url);
