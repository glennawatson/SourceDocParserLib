// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// A documented member of a type.
/// </summary>
/// <param name="Name">The display name.</param>
/// <param name="Uid">The documentation member ID.</param>
/// <param name="Kind">The kind of member.</param>
/// <param name="IsStatic">Whether the member is static.</param>
/// <param name="IsExtension">Whether the member is an extension method.</param>
/// <param name="IsRequired">Whether the member is required.</param>
/// <param name="IsVirtual">Whether the member is virtual.</param>
/// <param name="IsOverride">Whether the member is an override.</param>
/// <param name="IsAbstract">Whether the member is abstract.</param>
/// <param name="IsSealed">Whether the member is sealed.</param>
/// <param name="Signature">The formatted C# signature.</param>
/// <param name="Parameters">The member parameters.</param>
/// <param name="TypeParameters">The generic type parameters.</param>
/// <param name="ReturnType">The return type reference.</param>
/// <param name="ContainingTypeUid">The UID of the declaring type.</param>
/// <param name="ContainingTypeName">The name of the declaring type.</param>
/// <param name="SourceUrl">The source link URL.</param>
/// <param name="Documentation">The parsed XML documentation.</param>
/// <param name="IsObsolete">Whether the member is decorated with <c>[Obsolete]</c>.</param>
/// <param name="ObsoleteMessage">Message supplied to <c>[Obsolete(...)]</c>, or null.</param>
/// <param name="Attributes">Attributes applied to the member, in declaration order.</param>
public sealed record ApiMember(
    string Name,
    string Uid,
    ApiMemberKind Kind,
    bool IsStatic,
    bool IsExtension,
    bool IsRequired,
    bool IsVirtual,
    bool IsOverride,
    bool IsAbstract,
    bool IsSealed,
    string Signature,
    ApiParameter[] Parameters,
    string[] TypeParameters,
    ApiTypeReference? ReturnType,
    string ContainingTypeUid,
    string ContainingTypeName,
    string? SourceUrl,
    ApiDocumentation Documentation,
    bool IsObsolete,
    string? ObsoleteMessage,
    ApiAttribute[] Attributes);
