// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// A discriminated union (C# 15+ closed hierarchy). Structurally the
/// base type is a class implementing
/// <c>System.Runtime.CompilerServices.IUnion</c>; the
/// <see cref="Cases"/> list points at the case types (themselves
/// emitted as their own <see cref="ApiObjectType"/> records).
/// Members declared on the union base itself are kept on
/// <see cref="Members"/> for completeness — typically empty in
/// practice but available when the user adds shared methods.
/// </summary>
/// <param name="Name">The simple name.</param>
/// <param name="FullName">The namespace-qualified name.</param>
/// <param name="Uid">The documentation member ID.</param>
/// <param name="Namespace">The containing namespace.</param>
/// <param name="Arity">Number of generic type parameters.</param>
/// <param name="IsStatic">Whether the type is static.</param>
/// <param name="IsSealed">Whether the type is sealed.</param>
/// <param name="IsAbstract">Whether the type is abstract (always <see langword="true"/> for a closed union base).</param>
/// <param name="AssemblyName">The declaring assembly name.</param>
/// <param name="Documentation">The parsed XML documentation.</param>
/// <param name="BaseType">The immediate base type reference.</param>
/// <param name="Interfaces">Directly declared interfaces (includes the union marker interface).</param>
/// <param name="SourceUrl">The source link URL.</param>
/// <param name="AppliesTo">TFMs the type appears in.</param>
/// <param name="IsObsolete">Whether the type is decorated with <c>[Obsolete]</c>.</param>
/// <param name="ObsoleteMessage">Message supplied to <c>[Obsolete(...)]</c>, or null.</param>
/// <param name="Attributes">Attributes applied to the type, in declaration order.</param>
/// <param name="Members">Members declared on the union base itself (often empty).</param>
/// <param name="Cases">Case type references; each case is also walked as a sibling <see cref="ApiObjectType"/>.</param>
public sealed record ApiUnionType(
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
    ApiAttribute[] Attributes,
    ApiMember[] Members,
    ApiTypeReference[] Cases) : ApiType(
        Name,
        FullName,
        Uid,
        Namespace,
        Arity,
        IsStatic,
        IsSealed,
        IsAbstract,
        AssemblyName,
        Documentation,
        BaseType,
        Interfaces,
        SourceUrl,
        AppliesTo,
        IsObsolete,
        ObsoleteMessage,
        Attributes);
