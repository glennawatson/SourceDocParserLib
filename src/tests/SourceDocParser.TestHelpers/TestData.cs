// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.TestHelpers;

/// <summary>
/// Tiny factory helpers for assembling <see cref="ApiType"/> /
/// <see cref="ApiCatalog"/> instances without having to spell out every
/// record positional parameter at every call site. Shared across the
/// per-package test projects.
/// </summary>
public static class TestData
{
    /// <summary>
    /// Creates a minimal <see cref="ApiType"/> in a deterministic shape.
    /// </summary>
    /// <param name="uid">Unique identifier (also drives FullName when not supplied).</param>
    /// <param name="kind">Type kind.</param>
    /// <param name="assemblyName">Declaring assembly name.</param>
    /// <param name="sourceUrl">Optional SourceLink URL.</param>
    /// <returns>The constructed type.</returns>
    public static ApiType Type(
        string uid,
        ApiTypeKind kind = ApiTypeKind.Class,
        string assemblyName = "Test",
        string? sourceUrl = null) =>
        new(
            Name: uid,
            FullName: uid,
            Uid: uid,
            Namespace: string.Empty,
            Kind: kind,
            Arity: 0,
            IsStatic: false,
            IsSealed: false,
            IsAbstract: false,
            IsReadOnly: false,
            IsByRefLike: false,
            AssemblyName: assemblyName,
            Documentation: ApiDocumentation.Empty,
            BaseType: null,
            Interfaces: [],
            UnionCases: [],
            Members: [],
            SourceUrl: sourceUrl,
            AppliesTo: []);

    /// <summary>
    /// Creates an <see cref="ApiCatalog"/> for a TFM containing the supplied types.
    /// </summary>
    /// <param name="tfm">TFM the catalog represents.</param>
    /// <param name="types">Types in the catalog.</param>
    /// <returns>The constructed catalog.</returns>
    public static ApiCatalog Catalog(string tfm, params ApiType[] types) =>
        new(tfm, [.. types]);
}
