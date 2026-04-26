// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet;

/// <summary>
/// Describes a NuGet package whose assemblies are extracted purely as
/// reference assemblies for the docfx <c>UniversalAssemblyResolver</c>. These
/// are not documented themselves; they exist solely so that types referenced
/// by the documented packages can be resolved during metadata generation.
/// </summary>
/// <param name="Id">The NuGet package identifier (for example <c>Microsoft.NETCore.App.Ref</c>).</param>
/// <param name="Version">Optional pinned package version. When <see langword="null"/> the latest stable version is resolved at fetch time.</param>
/// <param name="TargetTfm">The TFM directory under <c>refs/</c> to drop the extracted assemblies into (for example <c>net10.0</c>). This is intentionally explicit because reference packages often ship a single canonical set of assemblies regardless of the package's own TFM layout.</param>
/// <param name="PathPrefix">Path prefix inside the <c>.nupkg</c> archive to extract from. Defaults to <c>ref</c>; some packages (notably the Mono Android workload) ship reference assemblies under <c>lib</c> instead.</param>
internal sealed record ReferencePackage(
    string Id,
    string? Version = null,
    string TargetTfm = "",
    string PathPrefix = "ref");
