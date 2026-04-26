// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical;

/// <summary>
/// Maps an assembly name (or assembly-name prefix) to the
/// per-package folder the Zensical emitter writes its pages
/// under. The user supplies an ordered list — first match wins.
/// </summary>
/// <param name="FolderName">The api/-relative folder name (e.g. <c>Splat</c>, <c>ReactiveUI</c>).</param>
/// <param name="AssemblyPrefix">Bare-id or id+dot prefix matched against <see cref="ApiType.AssemblyName"/>. <c>Splat</c> routes both <c>Splat.dll</c> and <c>Splat.Core.dll</c> into <see cref="FolderName"/>.</param>
public readonly record struct PackageRoutingRule(string FolderName, string AssemblyPrefix);
