// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Object-shaped type — class, struct, interface, record class, or
/// record struct. Carries a member list. The <see cref="Kind"/> field
/// distinguishes the underlying flavour for emitters that only need a
/// label change between them (e.g. the <c>class</c> vs <c>struct</c>
/// keyword on the type page).
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
/// <param name="Kind">Concrete object kind (class / struct / interface / record / record struct).</param>
/// <param name="IsReadOnly">Whether the type is <c>readonly</c> (struct/record-struct only; otherwise <see langword="false"/>).</param>
/// <param name="IsByRefLike">Whether the type is a <c>ref struct</c>.</param>
/// <param name="Members">Documented members declared on the type.</param>
public sealed record ApiObjectType(
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
    List<ApiTypeReference> Interfaces,
    string? SourceUrl,
    List<string> AppliesTo,
    ApiObjectKind Kind,
    bool IsReadOnly,
    bool IsByRefLike,
    List<ApiMember> Members) : ApiType(
        Name, FullName, Uid, Namespace, Arity, IsStatic, IsSealed, IsAbstract,
        AssemblyName, Documentation, BaseType, Interfaces, SourceUrl, AppliesTo);
