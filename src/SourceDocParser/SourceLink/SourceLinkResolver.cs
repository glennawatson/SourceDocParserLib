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
public sealed class SourceLinkResolver(string assemblyPath) : ISourceLinkResolver
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

        if (PickMethodForLocation(symbol) is not { MetadataToken: var token and not 0 })
        {
            return null;
        }

        if (_reader.GetMethodLocation(token) is not { } location)
        {
            return null;
        }

        return _reader.ResolveRawUrl(location.LocalPath) is { } rawUrl
            ? SourceUrlRewriter.ToBlobUrl(rawUrl, location.StartLine)
            : null;
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
        var members = type.GetMembers();
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (member is IMethodSymbol { MetadataToken: not 0 } method)
            {
                found = method;
                break;
            }
        }

        return _firstMethodCache[type] = found;
    }
}
