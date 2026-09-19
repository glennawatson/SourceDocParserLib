// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>A package version selected while resolving a documentation root.</summary>
/// <param name="Id">NuGet package identifier.</param>
/// <param name="Version">Resolved NuGet package version.</param>
[System.Diagnostics.DebuggerDisplay("{Id,nq}/{Version,nq}")]
public readonly record struct ApiPackageDependency(string Id, string Version);
