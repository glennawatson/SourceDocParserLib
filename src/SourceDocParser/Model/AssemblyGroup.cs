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
public sealed record AssemblyGroup(
    string Tfm,
    string[] AssemblyPaths,
    Dictionary<string, string> FallbackIndex);
