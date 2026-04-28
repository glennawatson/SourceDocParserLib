// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Zensical-flavoured wrapper over <see cref="CatalogIndexes"/>. The
/// algorithm lives in the core library; this shell only contributes
/// the System.Object baseline UIDs in the
/// commentId form the autoref / mkdocs renderer expects (with the
/// <c>M:</c> prefix).
/// </summary>
public sealed class ZensicalCatalogIndexes
{
    /// <summary>
    /// The well-known <see cref="object"/> members every class type
    /// inherits, in <c>M:</c>-prefixed commentId form so the resolver
    /// can route them to Microsoft Learn.
    /// </summary>
    private static readonly string[] _objectInheritedUids =
    [
        "M:System.Object.Equals(System.Object)",
        "M:System.Object.Equals(System.Object,System.Object)",
        "M:System.Object.GetHashCode",
        "M:System.Object.GetType",
        "M:System.Object.MemberwiseClone",
        "M:System.Object.ReferenceEquals(System.Object,System.Object)",
        "M:System.Object.ToString",
    ];

    /// <summary>The shared core indexes this wrapper delegates to.</summary>
    private readonly CatalogIndexes _core;

    /// <summary>Initializes a new instance of the <see cref="ZensicalCatalogIndexes"/> class wrapping <paramref name="core"/>.</summary>
    /// <param name="core">The underlying core indexes.</param>
    private ZensicalCatalogIndexes(CatalogIndexes core)
    {
        _core = core;
    }

    /// <summary>Gets the empty index bundle — used by callers that don't supply a catalog.</summary>
    public static ZensicalCatalogIndexes Empty { get; } = new(CatalogIndexes.Empty);

    /// <summary>
    /// Builds the indexes for <paramref name="types"/>, folding in
    /// the Zensical <see cref="object"/> baseline. Returns the
    /// shared <see cref="Empty"/> singleton for an empty input so
    /// reference-equality checks in tests stay stable.
    /// </summary>
    /// <param name="types">All types about to be rendered.</param>
    /// <returns>The frozen index bundle.</returns>
    public static ZensicalCatalogIndexes Build(ApiType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        return types is [] ? Empty : new(CatalogIndexes.Build(types, _objectInheritedUids));
    }

    /// <summary>Returns the derived-class refs for <paramref name="uid"/>; the shared empty array when none.</summary>
    /// <param name="uid">Type uid.</param>
    /// <returns>The derived class refs.</returns>
    public ApiTypeReference[] GetDerived(string uid) => _core.GetDerived(uid);

    /// <summary>Returns the extension methods that target <paramref name="uid"/>; empty when none.</summary>
    /// <param name="uid">Extended type uid.</param>
    /// <returns>The extension method members.</returns>
    public ApiMember[] GetExtensions(string uid) => _core.GetExtensions(uid);

    /// <summary>Returns the inherited member uids for <paramref name="uid"/>; empty when no entry.</summary>
    /// <param name="uid">Type uid.</param>
    /// <returns>The inherited member uids.</returns>
    public string[] GetInherited(string uid) => _core.GetInherited(uid);
}
