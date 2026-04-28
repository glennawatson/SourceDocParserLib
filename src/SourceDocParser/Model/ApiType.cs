// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// A documented type. Identity / hierarchy / docs / source-link state lives on this base.
/// </summary>
/// <param name="Name">The simple name.</param>
/// <param name="FullName">The namespace-qualified name.</param>
/// <param name="Uid">The documentation member ID.</param>
/// <param name="Namespace">The containing namespace.</param>
/// <param name="Arity">Number of generic type parameters.</param>
/// <param name="IsStatic">Whether the type is static.</param>
/// <param name="IsSealed">Whether the type is sealed.</param>
/// <param name="IsAbstract">Whether the type is abstract.</param>
/// <param name="AssemblyName">The declaring assembly name.</param>
/// <param name="Documentation">The parsed XML documentation.</param>
/// <param name="BaseType">The immediate base type reference, if any.</param>
/// <param name="Interfaces">Directly declared interfaces.</param>
/// <param name="SourceUrl">The source link URL.</param>
/// <param name="AppliesTo">TFMs the type appears in.</param>
/// <param name="IsObsolete">Whether the type is obsolete.</param>
/// <param name="ObsoleteMessage">Obsolete message.</param>
/// <param name="Attributes">Attributes applied to the type.</param>
public abstract record ApiType(
    string Name,
    string FullName,
    string Uid,
    string Namespace,
    int Arity,
    bool IsStatic,
    bool IsSealed,
    bool IsAbstract,
    string AssemblyName,
    ApiDocumentation Documentation,
    ApiTypeReference? BaseType,
    ApiTypeReference[] Interfaces,
    string? SourceUrl,
    string[] AppliesTo,
    bool IsObsolete,
    string? ObsoleteMessage,
    ApiAttribute[] Attributes)
{
    /// <summary>
    /// Gets the generic type parameter names declared on the type
    /// itself (e.g. <c>["TKey", "TValue"]</c> for
    /// <c>Dictionary&lt;TKey, TValue&gt;</c>). Empty when
    /// <see cref="Arity"/> is zero. Surfaced as a non-positional
    /// init property so existing construction sites stay
    /// non-breaking; the walker populates it via <c>with { TypeParameters = ... }</c>
    /// after building the base record.
    /// </summary>
    public string[] TypeParameters { get; init; } = [];
}
