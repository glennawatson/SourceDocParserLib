// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// A delegate. The Invoke signature is the entire useful surface, so
/// it's surfaced as <see cref="Invoke"/> rather than scattered through
/// generic members; emitters render it directly on the type page and
/// produce no per-overload pages for delegate types.
/// </summary>
/// <param name="Name">The simple name.</param>
/// <param name="FullName">The namespace-qualified name.</param>
/// <param name="Uid">The documentation member ID.</param>
/// <param name="Namespace">The containing namespace.</param>
/// <param name="Arity">Number of generic type parameters declared on the delegate itself.</param>
/// <param name="IsStatic">Whether the type is static.</param>
/// <param name="IsSealed">Whether the type is sealed.</param>
/// <param name="IsAbstract">Whether the type is abstract.</param>
/// <param name="AssemblyName">The declaring assembly name.</param>
/// <param name="Documentation">The parsed XML documentation.</param>
/// <param name="BaseType">The immediate base type reference (typically <c>System.MulticastDelegate</c>).</param>
/// <param name="Interfaces">Directly declared interfaces.</param>
/// <param name="SourceUrl">The source link URL.</param>
/// <param name="AppliesTo">TFMs the type appears in.</param>
/// <param name="Invoke">Invoke method signature (return type, parameters, type parameters).</param>
public sealed record ApiDelegateType(
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
    ApiDelegateSignature Invoke) : ApiType(
        Name, FullName, Uid, Namespace, Arity, IsStatic, IsSealed, IsAbstract,
        AssemblyName, Documentation, BaseType, Interfaces, SourceUrl, AppliesTo);
