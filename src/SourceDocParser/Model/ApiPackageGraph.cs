// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>The package identity and resolved dependencies for one documentation target.</summary>
/// <param name="Id">Documentation root's NuGet package identifier.</param>
/// <param name="Version">Documentation root's NuGet package version.</param>
/// <param name="TargetFramework">Framework for which the dependencies were resolved.</param>
/// <param name="AssemblyNames">Documented assemblies supplied by the root.</param>
/// <param name="Dependencies">Resolved dependencies, excluding the documentation root.</param>
[System.Diagnostics.DebuggerDisplay("{Id,nq}/{Version,nq} ({TargetFramework,nq})")]
public sealed record ApiPackageGraph(string Id, string Version, string TargetFramework, string[] AssemblyNames, ApiPackageDependency[] Dependencies)
{
    /// <summary>Gets the optional runtime identifier used for this target.</summary>
    public string? RuntimeIdentifier { get; init; }
}
