// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Loads a compiled .NET assembly into a Roslyn <see cref="CSharpCompilation"/>.
/// One loader is typically created per TFM group; the loader owns a cache of
/// previously-resolved <see cref="MetadataReference"/> instances so the BCL
/// ref pack is loaded once per group, not once per assembly.
/// </summary>
public interface ICompilationLoader : IDisposable
{
    /// <summary>
    /// Loads <paramref name="assemblyPath"/> and its transitive references
    /// into a Roslyn compilation.
    /// </summary>
    /// <param name="assemblyPath">Absolute path to the .dll to load.</param>
    /// <param name="fallbackReferences">Map from simple assembly name to absolute path used when the resolver cannot locate a reference on its own.</param>
    /// <param name="includePrivateMembers">When true, the compilation imports non-public members.</param>
    /// <returns>The compilation and the primary assembly symbol.</returns>
    (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(
        string assemblyPath,
        Dictionary<string, string> fallbackReferences,
        bool includePrivateMembers = false);
}
