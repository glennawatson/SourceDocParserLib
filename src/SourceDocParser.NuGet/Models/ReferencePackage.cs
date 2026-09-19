// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Models;

/// <summary>
/// Selects framework reference packs or additional assets from packages in a
/// documentation root's dependency graph. These declarations do not create API pages.
/// </summary>
/// <param name="Id">
/// The NuGet package identifier (for example
/// <c>Microsoft.NETCore.App.Ref</c>).
/// </param>
/// <param name="Version">
/// Optional framework-pack version. Ordinary dependencies use the version
/// resolved for their documentation root; use dependencyPins to override that graph explicitly.
/// </param>
/// <param name="TargetTfm">
/// Framework to which the reference-asset selection applies.
/// </param>
/// <param name="PathPrefix">
/// Path prefix inside the <c>.nupkg</c> archive to extract from. Defaults
/// to <c>ref</c>; some packages (notably the Mono Android workload) ship
/// reference assemblies under <c>lib</c> instead.
/// </param>
internal sealed record ReferencePackage(
    string Id,
    string? Version = null,
    string TargetTfm = "",
    string PathPrefix = "ref");
