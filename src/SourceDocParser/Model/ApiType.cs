// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// A documented type. Discriminated by sealed derivation:
/// <see cref="ApiObjectType"/> (class/struct/interface/record/record
/// struct), <see cref="ApiEnumType"/>, <see cref="ApiDelegateType"/>,
/// or <see cref="ApiUnionType"/>. Emitters pattern-match on the
/// derived type to access the kind-specific data; the shared
/// identity / hierarchy / docs / source-link state lives on this base.
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
    string[] AppliesTo);
