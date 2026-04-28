// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Frozen;
using SourceDocParser.Model;

namespace SourceDocParser;

/// <summary>
/// Per-emit catalog rollups: derived-class lookup, reverse extension-
/// method lookup, and inherited-member uid lists. Built once at the
/// start of an emit run so each render-time lookup is O(1).
/// </summary>
/// <remarks>
/// The inherited-member <c>objectInheritedUids</c> baseline (passed to
/// <see cref="Build(ApiType[], string[])"/>) is supplied per emitter
/// because the wire format differs: mkdocs-autorefs (Zensical) wants
/// the full <c>M:</c>-prefixed commentId, docfx's xrefmap wants the
/// bare member name. The algorithm is the same; only the baseline
/// strings differ.
/// </remarks>
public sealed class CatalogIndexes
{
    /// <summary>Initializes a new instance of the <see cref="CatalogIndexes"/> class from pre-built frozen lookups.</summary>
    /// <param name="derivedClasses">Reverse base-type lookup.</param>
    /// <param name="extensionMethods">Reverse first-parameter-type lookup over extension methods.</param>
    /// <param name="inheritedMembers">Per-type inherited member uid list.</param>
    private CatalogIndexes(
        FrozenDictionary<string, ApiTypeReference[]> derivedClasses,
        FrozenDictionary<string, ApiMember[]> extensionMethods,
        FrozenDictionary<string, string[]> inheritedMembers)
    {
        DerivedClasses = derivedClasses;
        ExtensionMethods = extensionMethods;
        InheritedMembers = inheritedMembers;
    }

    /// <summary>Gets the empty index bundle -- used when no catalog is supplied.</summary>
    public static CatalogIndexes Empty { get; } = new(
        FrozenDictionary<string, ApiTypeReference[]>.Empty,
        FrozenDictionary<string, ApiMember[]>.Empty,
        FrozenDictionary<string, string[]>.Empty);

    /// <summary>Gets the reverse base-type lookup keyed on type uid.</summary>
    public FrozenDictionary<string, ApiTypeReference[]> DerivedClasses { get; }

    /// <summary>Gets the reverse extension-method lookup keyed on the extended type's uid.</summary>
    public FrozenDictionary<string, ApiMember[]> ExtensionMethods { get; }

    /// <summary>Gets the inherited-member uid lists keyed on type uid.</summary>
    public FrozenDictionary<string, string[]> InheritedMembers { get; }

    /// <summary>
    /// Builds all three indexes in a single O(N) + O(NxMext) sweep
    /// over <paramref name="types"/>. Compiler-generated symbols are
    /// skipped so display-class artefacts don't appear in any rollup.
    /// </summary>
    /// <param name="types">All types about to be rendered.</param>
    /// <param name="objectInheritedUids">Baseline <see cref="object"/> member uids appended to every class / record's inherited list. Format is emitter-specific.</param>
    /// <returns>The frozen index bundle.</returns>
    public static CatalogIndexes Build(ApiType[] types, string[] objectInheritedUids)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(objectInheritedUids);
        if (types is [])
        {
            return Empty;
        }

        var typesByUid = BuildTypesByUid(types);
        var derivedRaw = BuildDerivedRaw(types);
        var extensionsRaw = BuildExtensionsRaw(types);
        var inheritedRaw = BuildInheritedRaw(types, typesByUid, objectInheritedUids);

        return new(
            FreezeArrays(derivedRaw),
            FreezeArrays(extensionsRaw),
            FreezeArrays(inheritedRaw));
    }

    /// <summary>Returns the derived-class refs for <paramref name="uid"/>; the shared empty array when none.</summary>
    /// <param name="uid">Type uid.</param>
    /// <returns>The derived class refs.</returns>
    public ApiTypeReference[] GetDerived(string uid) =>
        DerivedClasses.TryGetValue(uid, out var refs) ? refs : [];

    /// <summary>Returns the extension methods that target <paramref name="uid"/>; empty when none.</summary>
    /// <param name="uid">Extended type uid.</param>
    /// <returns>The extension method members.</returns>
    public ApiMember[] GetExtensions(string uid) =>
        ExtensionMethods.TryGetValue(uid, out var members) ? members : [];

    /// <summary>Returns the inherited member uids for <paramref name="uid"/>; empty when no entry.</summary>
    /// <param name="uid">Type uid.</param>
    /// <returns>The inherited member uids.</returns>
    public string[] GetInherited(string uid) =>
        InheritedMembers.TryGetValue(uid, out var uids) ? uids : [];

    /// <summary>Builds the per-uid <c>ApiObjectType</c> lookup used by the inherited-members pass.</summary>
    /// <param name="types">All types being emitted.</param>
    /// <returns>Dictionary keyed on type uid.</returns>
    internal static Dictionary<string, ApiObjectType> BuildTypesByUid(ApiType[] types)
    {
        var map = new Dictionary<string, ApiObjectType>(types.Length, StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is ApiObjectType { Uid: [_, ..] uid } obj && !IsCompilerGenerated(obj.Name))
            {
                map[uid] = obj;
            }
        }

        return map;
    }

    /// <summary>Single-pass build of the reverse base-type lookup.</summary>
    /// <param name="types">All types being emitted.</param>
    /// <returns>Mutable dictionary; converted to frozen form by the caller.</returns>
    internal static Dictionary<string, List<ApiTypeReference>> BuildDerivedRaw(ApiType[] types)
    {
        var map = new Dictionary<string, List<ApiTypeReference>>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (IsCompilerGenerated(type.Name))
            {
                continue;
            }

            if (type.BaseType is not { Uid: [_, ..] baseUid })
            {
                continue;
            }

            AddToBucket(map, baseUid, new ApiTypeReference(type.Name, type.Uid));
        }

        return map;
    }

    /// <summary>
    /// Single-pass build of the reverse extension-method lookup.
    /// </summary>
    /// <remarks>
    /// Best-effort: only extension methods whose first parameter has
    /// a concrete type uid are indexed. Generic-receiver extensions
    /// of the form <c>static void Foo&lt;T>(this T self) where T : IBar</c>
    /// are skipped -- docfx itself propagates these onto every type
    /// satisfying the constraint, but emitters here follow the same
    /// best-effort policy so the outputs stay aligned.
    /// </remarks>
    /// <param name="types">All types being emitted.</param>
    /// <returns>Mutable dictionary; converted to frozen form by the caller.</returns>
    internal static Dictionary<string, List<ApiMember>> BuildExtensionsRaw(ApiType[] types)
    {
        var map = new Dictionary<string, List<ApiMember>>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is not ApiObjectType { IsStatic: true, Members: [_, ..] members })
            {
                continue;
            }

            for (var m = 0; m < members.Length; m++)
            {
                var member = members[m];
                if (!member.IsExtension || member.Parameters is [] || IsCompilerGenerated(member.Name))
                {
                    continue;
                }

                var extendedUid = member.Parameters[0].Type.Uid;
                if (extendedUid is not [_, ..])
                {
                    continue;
                }

                AddToBucket(map, extendedUid, member);
            }
        }

        return map;
    }

    /// <summary>
    /// Builds inherited-member uid lists for every class / record type.
    /// Folds in members of the immediate base when it lives in our
    /// walked set; appends <paramref name="objectInheritedUids"/> as
    /// the universal <see cref="object"/> baseline.
    /// </summary>
    /// <param name="types">All types being emitted.</param>
    /// <param name="typesByUid">Pre-built lookup from <see cref="BuildTypesByUid"/>.</param>
    /// <param name="objectInheritedUids">Baseline <see cref="object"/> member uids appended to every class / record.</param>
    /// <returns>Mutable dictionary; converted to frozen form by the caller.</returns>
    internal static Dictionary<string, List<string>> BuildInheritedRaw(
        ApiType[] types,
        Dictionary<string, ApiObjectType> typesByUid,
        string[] objectInheritedUids)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is not ApiObjectType { Kind: ApiObjectKind.Class or ApiObjectKind.Record } cls)
            {
                continue;
            }

            if (IsCompilerGenerated(cls.Name) || cls.Uid is not [_, ..] uid)
            {
                continue;
            }

            var inherited = new List<string>(objectInheritedUids.Length + 8);

            if (cls.BaseType is { Uid: [_, ..] baseUid }
                && typesByUid.TryGetValue(baseUid, out var baseType))
            {
                AppendBaseMemberUids(inherited, baseType);
            }

            for (var k = 0; k < objectInheritedUids.Length; k++)
            {
                inherited.Add(objectInheritedUids[k]);
            }

            map[uid] = inherited;
        }

        return map;
    }

    /// <summary>Pushes every non-compiler-generated member uid of <paramref name="baseType"/> onto <paramref name="destination"/>.</summary>
    /// <param name="destination">Inherited-member accumulator.</param>
    /// <param name="baseType">Walked base type whose members to fold in.</param>
    internal static void AppendBaseMemberUids(List<string> destination, ApiObjectType baseType)
    {
        var members = baseType.Members;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (IsCompilerGenerated(member.Name) || member.Uid is not [_, ..] uid)
            {
                continue;
            }

            destination.Add(uid);
        }
    }

    /// <summary>Converts a mutable per-key list dictionary into the frozen form consumed by render-time lookups.</summary>
    /// <typeparam name="T">Element type of the per-key list.</typeparam>
    /// <param name="raw">Mutable dictionary built during the catalog scan.</param>
    /// <returns>The frozen dictionary; empty input returns the shared empty frozen instance.</returns>
    internal static FrozenDictionary<string, T[]> FreezeArrays<T>(Dictionary<string, List<T>> raw)
    {
        if (raw.Count is 0)
        {
            return FrozenDictionary<string, T[]>.Empty;
        }

        var staged = new Dictionary<string, T[]>(raw.Count, StringComparer.Ordinal);
        foreach (var (key, list) in raw)
        {
            staged[key] = [.. list];
        }

        return staged.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Appends <paramref name="value"/> to the per-key bucket, creating the list on first use.</summary>
    /// <typeparam name="TVal">Element type.</typeparam>
    /// <param name="map">Bucket dictionary.</param>
    /// <param name="key">Key.</param>
    /// <param name="value">Element to append.</param>
    private static void AddToBucket<TVal>(Dictionary<string, List<TVal>> map, string key, TVal value)
    {
        if (!map.TryGetValue(key, out var bucket))
        {
            bucket = [];
            map[key] = bucket;
        }

        bucket.Add(value);
    }

    /// <summary>
    /// Tests whether a metadata <paramref name="name"/> is a
    /// compiler-generated artefact (display class, async / iterator
    /// state machine, anonymous type, lambda closure, backing field).
    /// Mirrors the docfx heuristic of "any name containing an angle
    /// bracket is mangled". Local check so the core library stays
    /// independent of <c>SourceDocParser.Common</c>.
    /// </summary>
    /// <param name="name">The symbol's metadata name.</param>
    /// <returns>True when the symbol should be skipped.</returns>
    private static bool IsCompilerGenerated(string name) =>
        name.AsSpan().IndexOfAny('<', '>') >= 0;
}
