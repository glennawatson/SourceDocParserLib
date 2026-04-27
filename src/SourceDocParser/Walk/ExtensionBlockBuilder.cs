// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using SourceDocParser.Model;

namespace SourceDocParser.Walk;

/// <summary>
/// Detects the synthesised C# 14 extension marker types nested
/// under a static container and converts each to an
/// <see cref="ApiExtensionBlock"/> — receiver parameter (name + type)
/// plus the conceptual members declared inside the block.
/// The classic <c>[Extension]</c> implementation methods on the
/// parent container come through <see cref="MemberBuilder.Build"/>
/// independently and aren't touched here.
/// </summary>
internal static class ExtensionBlockBuilder
{
    /// <summary>
    /// Walks <paramref name="type"/>'s nested types looking for
    /// <see cref="INamedTypeSymbol.IsExtension"/> markers and
    /// converts each. Returns the shared empty array when the type
    /// has no extension declarations (the dominant case) so the
    /// allocation pattern is zero-cost on the common path.
    /// </summary>
    /// <param name="type">Container type whose extension blocks to collect.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The extension blocks declared on the type.</returns>
    internal static ApiExtensionBlock[] Build(INamedTypeSymbol type, SymbolWalkContext context)
    {
        // C# 14 extension blocks only land on static container types,
        // so skip the GetTypeMembers materialisation for the dominant
        // non-static case. Roslyn allocates the nested-types
        // ImmutableArray on every call regardless of how many entries
        // it holds; the IsStatic guard makes the common no-op path a
        // single property read.
        if (!type.IsStatic)
        {
            return [];
        }

        var nested = type.GetTypeMembers();
        if (nested.IsDefaultOrEmpty)
        {
            return [];
        }

        List<ApiExtensionBlock> blocks = [];
        for (var i = 0; i < nested.Length; i++)
        {
            var marker = nested[i];
            if (TryBuildBlock(marker, context) is { } block)
            {
                blocks.Add(block);
            }
        }

        return blocks.Count is 0 ? [] : [.. blocks];
    }

    /// <summary>
    /// Tries to produce an <see cref="ApiExtensionBlock"/> from a
    /// single nested type. Returns null when the type isn't a C# 14
    /// extension marker, or when its receiver parameter is missing
    /// (which happens for malformed metadata).
    /// </summary>
    /// <param name="marker">Candidate nested type.</param>
    /// <param name="context">Per-walk state bundle.</param>
    /// <returns>The block, or null when the marker doesn't apply.</returns>
    internal static ApiExtensionBlock? TryBuildBlock(INamedTypeSymbol marker, SymbolWalkContext context)
    {
        if (!SymbolWalkerHelpers.IsExtensionDeclaration(marker))
        {
            return null;
        }

        if (marker.ExtensionParameter is not { } receiverParam)
        {
            return null;
        }

        var receiverRef = context.TypeRefs.GetOrAdd(receiverParam.Type, SymbolWalkerHelpers.BuildReference);
        var blockMembers = MemberBuilder.Build(marker, marker.Name, marker.GetDocumentationCommentId() ?? string.Empty, context);
        return new ApiExtensionBlock(receiverParam.Name, receiverRef, blockMembers);
    }
}
