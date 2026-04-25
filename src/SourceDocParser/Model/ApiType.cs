// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// A documented type.
/// </summary>
/// <param name="Name">The simple name.</param>
/// <param name="FullName">The namespace-qualified name.</param>
/// <param name="Uid">The documentation member ID.</param>
/// <param name="Namespace">The containing namespace.</param>
/// <param name="Kind">The kind of type.</param>
/// <param name="Arity">Number of generic type parameters.</param>
/// <param name="IsStatic">Whether the type is static.</param>
/// <param name="IsSealed">Whether the type is sealed.</param>
/// <param name="IsAbstract">Whether the type is abstract.</param>
/// <param name="IsReadOnly">Whether the type is readonly.</param>
/// <param name="IsByRefLike">Whether the type is a ref struct.</param>
/// <param name="AssemblyName">The declaring assembly name.</param>
/// <param name="Documentation">The parsed XML documentation.</param>
/// <param name="BaseType">The immediate base type reference.</param>
/// <param name="Interfaces">Directly declared interfaces.</param>
/// <param name="UnionCases">Case types for union types.</param>
/// <param name="Members">Documented members.</param>
/// <param name="SourceUrl">The source link URL.</param>
/// <param name="AppliesTo">TFMs the type appears in.</param>
public sealed record ApiType(
    string Name,
    string FullName,
    string Uid,
    string Namespace,
    ApiTypeKind Kind,
    int Arity,
    bool IsStatic,
    bool IsSealed,
    bool IsAbstract,
    bool IsReadOnly,
    bool IsByRefLike,
    string AssemblyName,
    ApiDocumentation Documentation,
    ApiTypeReference? BaseType,
    List<ApiTypeReference> Interfaces,
    List<ApiTypeReference> UnionCases,
    List<ApiMember> Members,
    string? SourceUrl,
    List<string> AppliesTo);
