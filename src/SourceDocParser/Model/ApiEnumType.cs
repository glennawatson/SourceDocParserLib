// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// An enum. The values are surfaced as a structured list rather than
/// as generic members so emitters can render them inline on the type
/// page; per-value markdown pages would explode the file count for any
/// large vocabulary (icon-font enums in particular).
/// </summary>
/// <param name="Name">The simple name.</param>
/// <param name="FullName">The namespace-qualified name.</param>
/// <param name="Uid">The documentation member ID.</param>
/// <param name="Namespace">The containing namespace.</param>
/// <param name="Arity">Number of generic type parameters (always zero for enums).</param>
/// <param name="IsStatic">Whether the type is static.</param>
/// <param name="IsSealed">Whether the type is sealed.</param>
/// <param name="IsAbstract">Whether the type is abstract.</param>
/// <param name="AssemblyName">The declaring assembly name.</param>
/// <param name="Documentation">The parsed XML documentation.</param>
/// <param name="BaseType">The immediate base type reference, if any.</param>
/// <param name="Interfaces">Directly declared interfaces (empty for enums in practice).</param>
/// <param name="SourceUrl">The source link URL.</param>
/// <param name="AppliesTo">TFMs the type appears in.</param>
/// <param name="UnderlyingType">Reference to the integral storage type (e.g. <c>System.Int32</c>).</param>
/// <param name="Values">Declared enum values in source order.</param>
public sealed record ApiEnumType(
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
    ApiTypeReference UnderlyingType,
    ApiEnumValue[] Values) : ApiType(
        Name, FullName, Uid, Namespace, Arity, IsStatic, IsSealed, IsAbstract,
        AssemblyName, Documentation, BaseType, Interfaces, SourceUrl, AppliesTo);
