// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// Returns the implicit BCL base type each kind inherits from.
/// The walker filters <see cref="object"/> / <see cref="ValueType"/>
/// / <see cref="Enum"/> / <see cref="MulticastDelegate"/> from
/// <see cref="ApiType.BaseType"/> because most consumers don't want
/// the noise — but docfx itself always emits them under
/// <c>inheritance:</c> and as a <c>references:</c> entry. We
/// synthesise them here so the YAML output matches docfx without
/// having to change the model.
/// All references are <see langword="static readonly"/> singletons,
/// so the per-page allocation cost is zero.
/// </summary>
internal static class DocfxWellKnownBases
{
    /// <summary>Reusable <see cref="object"/> reference — every class type's implicit base.</summary>
    private static readonly ApiTypeReference _objectRef = new("Object", "T:System.Object");

    /// <summary>Reusable <see cref="ValueType"/> reference — every struct's implicit base.</summary>
    private static readonly ApiTypeReference _valueTypeRef = new("ValueType", "T:System.ValueType");

    /// <summary>Reusable <see cref="Enum"/> reference — every enum's implicit base.</summary>
    private static readonly ApiTypeReference _enumRef = new("Enum", "T:System.Enum");

    /// <summary>Reusable <see cref="MulticastDelegate"/> reference — every delegate's implicit base.</summary>
    private static readonly ApiTypeReference _multicastDelegateRef = new("MulticastDelegate", "T:System.MulticastDelegate");

    /// <summary>
    /// Returns the implicit BCL base for <paramref name="type"/>, or
    /// <see langword="null"/> when the kind has no implicit base
    /// (interfaces, unions). The returned reference is a static
    /// singleton — callers can stash it without copying.
    /// </summary>
    /// <param name="type">Type whose implicit base to look up.</param>
    /// <returns>The shared base reference, or <see langword="null"/>.</returns>
    public static ApiTypeReference? For(ApiType type) => type switch
    {
        ApiObjectType { Kind: ApiObjectKind.Class or ApiObjectKind.Record } => _objectRef,
        ApiObjectType { Kind: ApiObjectKind.Struct or ApiObjectKind.RecordStruct } => _valueTypeRef,
        ApiEnumType => _enumRef,
        ApiDelegateType => _multicastDelegateRef,
        _ => null,
    };
}
