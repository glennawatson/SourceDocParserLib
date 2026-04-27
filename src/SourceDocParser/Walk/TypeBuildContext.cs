// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using SourceDocParser.Model;

namespace SourceDocParser.Walk;

/// <summary>
/// Bundle of pre-resolved per-type build inputs threaded through
/// <see cref="TypeBuilder"/>'s kind-specific branches. Lifted out to
/// keep each branch's signature short — they all consume the same
/// shape, only the assembled <see cref="ApiType"/> differs. As a
/// readonly record struct it stack-allocates, so passing it by
/// <c>in</c> at every call site costs the same as passing the
/// individual fields would have (one ref instead of twelve stack
/// slots) without any per-call heap pressure.
/// </summary>
/// <param name="Type">Source type symbol.</param>
/// <param name="Uid">Documentation comment id.</param>
/// <param name="Namespace">Containing namespace display string.</param>
/// <param name="FullName">Namespace-qualified name.</param>
/// <param name="Documentation">Pre-resolved documentation.</param>
/// <param name="BaseTypeRef">Base type reference.</param>
/// <param name="Interfaces">Interface references.</param>
/// <param name="SourceUrl">Source URL or null.</param>
/// <param name="Attributes">Pre-extracted attributes.</param>
/// <param name="IsObsolete">Whether <c>[Obsolete]</c> was applied.</param>
/// <param name="ObsoleteMessage">Optional <c>[Obsolete]</c> message.</param>
/// <param name="Context">Per-walk state bundle.</param>
internal readonly record struct TypeBuildContext(
    INamedTypeSymbol Type,
    string Uid,
    string Namespace,
    string FullName,
    ApiDocumentation Documentation,
    ApiTypeReference? BaseTypeRef,
    ApiTypeReference[] Interfaces,
    string? SourceUrl,
    ApiAttribute[] Attributes,
    bool IsObsolete,
    string? ObsoleteMessage,
    SymbolWalkContext Context);
