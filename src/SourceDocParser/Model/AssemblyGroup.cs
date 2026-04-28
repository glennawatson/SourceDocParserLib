// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// One TFM's assemblies, plus a fallback index used to resolve
/// transitive references the standard resolver can't find on its own.
/// </summary>
/// <param name="Tfm">TFM the assemblies were built for.</param>
/// <param name="AssemblyPaths">Absolute paths to the .dll files to walk.</param>
/// <param name="FallbackIndex">Filename to absolute path map for resolver fallback.</param>
/// <param name="BroadcastTfms">
/// TFMs whose public surface is a subset of this group's surface. The
/// pipeline skips a Roslyn walk for each listed TFM and instead
/// broadcasts this group's walked types into the merger stamped with
/// each broadcast TFM so <see cref="ApiType.AppliesTo"/> still records
/// every TFM the type applies to.
/// </param>
public sealed record AssemblyGroup(
    string Tfm,
    string[] AssemblyPaths,
    Dictionary<string, string> FallbackIndex,
    string[] BroadcastTfms)
{
    /// <summary>Initializes a new instance of the <see cref="AssemblyGroup"/> class for a single-TFM walk with no broadcast targets.</summary>
    /// <param name="tfm">TFM the assemblies were built for.</param>
    /// <param name="assemblyPaths">Absolute paths to the .dll files to walk.</param>
    /// <param name="fallbackIndex">Filename to absolute path map for resolver fallback.</param>
    public AssemblyGroup(string tfm, string[] assemblyPaths, Dictionary<string, string> fallbackIndex)
        : this(tfm, assemblyPaths, fallbackIndex, [])
    {
    }
}
