// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.SourceLink;

/// <summary>
/// Resolves Roslyn symbols to source URLs by combining a PDB's per-method
/// debug information with the assembly's SourceLink map. Implementations are
/// scoped to a single assembly: the PDB is opened at construction and held
/// until <see cref="IDisposable.Dispose"/>.
/// </summary>
public interface ISourceLinkResolver : IDisposable
{
    /// <summary>
    /// Resolves <paramref name="symbol"/> to a source URL with a line anchor.
    /// </summary>
    /// <param name="symbol">The symbol to resolve.</param>
    /// <returns>A source URL, or null when no SourceLink data is available, the symbol cannot be mapped to a method, or its source location is missing from the PDB.</returns>
    string? Resolve(ISymbol symbol);
}
