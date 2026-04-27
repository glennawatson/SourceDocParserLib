// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.TestHelpers;

/// <summary>
/// Tiny factory helpers for assembling <see cref="ApiType"/> /
/// <see cref="ApiCatalog"/> instances without having to spell out every
/// record positional parameter at every call site. Shared across the
/// per-package test projects.
/// </summary>
public static class TestData
{
    /// <summary>
    /// Creates a minimal <see cref="ApiObjectType"/> in a deterministic
    /// shape using the default class kind and assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier (also drives FullName when not supplied).</param>
    /// <returns>The constructed object type.</returns>
    public static ApiObjectType ObjectType(string uid) =>
        ObjectType(uid, ApiObjectKind.Class, "Test", null);

    /// <summary>
    /// Creates a minimal <see cref="ApiObjectType"/> in a deterministic
    /// shape using the default class kind and supplied assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier (also drives FullName when not supplied).</param>
    /// <param name="assemblyName">Declaring assembly name.</param>
    /// <returns>The constructed object type.</returns>
    public static ApiObjectType ObjectType(string uid, string assemblyName) =>
        ObjectType(uid, ApiObjectKind.Class, assemblyName, null);

    /// <summary>
    /// Creates a minimal <see cref="ApiObjectType"/> in a deterministic
    /// shape using the supplied kind and default assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier (also drives FullName when not supplied).</param>
    /// <param name="kind">Object kind (class / struct / interface / record / record struct).</param>
    /// <returns>The constructed object type.</returns>
    public static ApiObjectType ObjectType(string uid, ApiObjectKind kind) =>
        ObjectType(uid, kind, "Test", null);

    /// <summary>
    /// Creates a minimal <see cref="ApiObjectType"/> in a deterministic
    /// shape using the supplied kind and assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier (also drives FullName when not supplied).</param>
    /// <param name="kind">Object kind (class / struct / interface / record / record struct).</param>
    /// <param name="assemblyName">Declaring assembly name.</param>
    /// <returns>The constructed object type.</returns>
    public static ApiObjectType ObjectType(string uid, ApiObjectKind kind, string assemblyName) =>
        ObjectType(uid, kind, assemblyName, null);

    /// <summary>
    /// Creates a minimal <see cref="ApiObjectType"/> in a deterministic
    /// shape — defaults to a class with no members.
    /// </summary>
    /// <param name="uid">Unique identifier (also drives FullName when not supplied).</param>
    /// <param name="kind">Object kind (class / struct / interface / record / record struct).</param>
    /// <param name="assemblyName">Declaring assembly name.</param>
    /// <param name="sourceUrl">Optional SourceLink URL.</param>
    /// <returns>The constructed object type.</returns>
    public static ApiObjectType ObjectType(
        string uid,
        ApiObjectKind kind,
        string assemblyName,
        string? sourceUrl) =>
        new(
            Name: uid,
            FullName: uid,
            Uid: uid,
            Namespace: string.Empty,
            Arity: 0,
            IsStatic: false,
            IsSealed: false,
            IsAbstract: false,
            AssemblyName: assemblyName,
            Documentation: ApiDocumentation.Empty,
            BaseType: null,
            Interfaces: [],
            SourceUrl: sourceUrl,
            AppliesTo: [],
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: [],
            Kind: kind,
            IsReadOnly: false,
            IsByRefLike: false,
            Members: [],
            ExtensionBlocks: []);

    /// <summary>
    /// Creates a minimal <see cref="ApiEnumType"/> in a deterministic
    /// shape using the default assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier.</param>
    /// <returns>The constructed enum type with no values.</returns>
    public static ApiEnumType EnumType(string uid) =>
        EnumType(uid, "Test");

    /// <summary>
    /// Creates a minimal <see cref="ApiEnumType"/> in a deterministic shape.
    /// </summary>
    /// <param name="uid">Unique identifier.</param>
    /// <param name="assemblyName">Declaring assembly name.</param>
    /// <returns>The constructed enum type with no values.</returns>
    public static ApiEnumType EnumType(string uid, string assemblyName) =>
        new(
            Name: uid,
            FullName: uid,
            Uid: uid,
            Namespace: string.Empty,
            Arity: 0,
            IsStatic: false,
            IsSealed: true,
            IsAbstract: false,
            AssemblyName: assemblyName,
            Documentation: ApiDocumentation.Empty,
            BaseType: null,
            Interfaces: [],
            SourceUrl: null,
            AppliesTo: [],
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: [],
            UnderlyingType: new("int", "T:System.Int32"),
            Values: []);

    /// <summary>
    /// Creates a minimal <see cref="ApiDelegateType"/> in a deterministic
    /// shape using the default assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier.</param>
    /// <returns>The constructed delegate type with a void Invoke signature.</returns>
    public static ApiDelegateType DelegateType(string uid) =>
        DelegateType(uid, "Test");

    /// <summary>
    /// Creates a minimal <see cref="ApiDelegateType"/> in a deterministic shape.
    /// </summary>
    /// <param name="uid">Unique identifier.</param>
    /// <param name="assemblyName">Declaring assembly name.</param>
    /// <returns>The constructed delegate type with a void Invoke signature.</returns>
    public static ApiDelegateType DelegateType(string uid, string assemblyName) =>
        new(
            Name: uid,
            FullName: uid,
            Uid: uid,
            Namespace: string.Empty,
            Arity: 0,
            IsStatic: false,
            IsSealed: true,
            IsAbstract: false,
            AssemblyName: assemblyName,
            Documentation: ApiDocumentation.Empty,
            BaseType: null,
            Interfaces: [],
            SourceUrl: null,
            AppliesTo: [],
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: [],
            Invoke: new($"void {uid}()", null, [], []));

    /// <summary>
    /// Creates an <see cref="ApiCatalog"/> for a TFM containing the supplied types.
    /// </summary>
    /// <param name="tfm">TFM the catalog represents.</param>
    /// <param name="types">Types in the catalog.</param>
    /// <returns>The constructed catalog.</returns>
    public static ApiCatalog Catalog(string tfm, params ApiType[] types) =>
        new(tfm, [.. types]);

    /// <summary>
    /// Creates a minimal class-shaped <see cref="ApiObjectType"/>
    /// using the default assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier.</param>
    /// <returns>The constructed type.</returns>
    public static ApiObjectType Type(string uid) =>
        Type(uid, "Test", null);

    /// <summary>
    /// Creates a minimal class-shaped <see cref="ApiObjectType"/>
    /// using the supplied assembly name.
    /// </summary>
    /// <param name="uid">Unique identifier.</param>
    /// <param name="assemblyName">Declaring assembly name.</param>
    /// <returns>The constructed type.</returns>
    public static ApiObjectType Type(string uid, string assemblyName) =>
        Type(uid, assemblyName, null);

    /// <summary>
    /// Constructs an <see cref="ApiObjectType"/> instance with a specific class kind, unique identifier,
    /// assembly name, and optional source URL for the type's definition.
    /// </summary>
    /// <param name="uid">The unique identifier of the type, which determines the FullName if not explicitly supplied.</param>
    /// <param name="assemblyName">The name of the assembly where the type is defined.</param>
    /// <param name="sourceUrl">An optional URL pointing to the source code location of the type definition.</param>
    /// <returns>An <see cref="ApiObjectType"/> representing the constructed type.</returns>
    public static ApiObjectType Type(
        string uid,
        string assemblyName,
        string? sourceUrl) =>
        ObjectType(uid, ApiObjectKind.Class, assemblyName, sourceUrl);
}
