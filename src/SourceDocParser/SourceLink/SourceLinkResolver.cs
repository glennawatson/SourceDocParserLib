// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.SourceLink;

/// <summary>
/// Resolves symbols to source URLs using PDB and SourceLink data.
/// </summary>
/// <remarks>
/// One resolver is typically created per assembly. The PDB is opened once at construction.
/// </remarks>
/// <param name="assemblyPath">Absolute path to the .dll.</param>
internal sealed class SourceLinkResolver(string assemblyPath) : IDisposable
{
    /// <summary>
    /// The underlying PDB reader.
    /// </summary>
    private readonly SourceLinkReader _reader = new(assemblyPath);

    /// <summary>
    /// Cache of the first method found on a type.
    /// </summary>
    private readonly Dictionary<INamedTypeSymbol, IMethodSymbol?> _firstMethodCache = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Resolves the supplied symbol to a source URL.
    /// </summary>
    /// <param name="symbol">The symbol to resolve.</param>
    /// <returns>A source URL with line anchor, or null if resolution fails.</returns>
    public string? Resolve(ISymbol symbol)
    {
        if (!_reader.HasSourceLink)
        {
            return null;
        }

        if (PickMethodForLocation(symbol) is not { } method)
        {
            return null;
        }

        if (_reader.GetMethodLocation(method.MetadataToken) is not { } location)
        {
            return null;
        }

        return _reader.ResolveRawUrl(location.LocalPath) is not { } rawUrl
            ? null
            : SourceUrlRewriter.ToBlobUrl(rawUrl, location.StartLine);
    }

    /// <inheritdoc/>
    public void Dispose() => _reader.Dispose();

    /// <summary>
    /// Picks the method symbol that best represents the symbol's source location.
    /// </summary>
    /// <remarks>
    /// Properties and events reference their accessors. Fields and types fall back to the first method on the containing type.
    /// </remarks>
    /// <param name="symbol">The symbol whose source line to find.</param>
    /// <returns>The representing method symbol, or null.</returns>
    private IMethodSymbol? PickMethodForLocation(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method,
        IPropertySymbol { GetMethod: { } getter } => getter,
        IPropertySymbol { SetMethod: { } setter } => setter,
        IEventSymbol { AddMethod: { } adder } => adder,
        IEventSymbol { RemoveMethod: { } remover } => remover,
        IFieldSymbol field => FirstMethodOnContainingType(field.ContainingType),
        INamedTypeSymbol type => FirstMethodOnContainingType(type),
        _ => null,
    };

    /// <summary>
    /// Finds the first method on a type with a valid metadata token.
    /// </summary>
    /// <param name="type">The type to scan.</param>
    /// <returns>The first method symbol found, or null.</returns>
    private IMethodSymbol? FirstMethodOnContainingType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        if (_firstMethodCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        IMethodSymbol? found = null;
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol { MetadataToken: not 0 } method)
            {
                continue;
            }

            found = method;
            break;
        }

        _firstMethodCache[type] = found;
        return found;
    }
}
