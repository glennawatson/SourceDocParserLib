// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Frozen;

namespace SourceDocParser.Zensical;

/// <summary>
/// Per-emit catalog rollups consumed by <see cref="TypePageEmitter"/>
/// to render the "Derived types", "Inherited members", and
/// "Extension methods" markdown sections. Built once at the start of
/// <see cref="ZensicalDocumentationEmitter.EmitAsync"/> and reused
/// by every per-type render. Lookups return <see cref="Array.Empty{T}"/>
/// (the shared singleton, no per-query allocation) when the type
/// has no entry. Mirrors the docfx-side
/// <c>SourceDocParser.Docfx.DocfxCatalogIndexes</c> shape so both
/// emitters can stay independent of one another.
/// </summary>
public sealed class ZensicalCatalogIndexes
{
    /// <summary>The well-known <see cref="object"/> members every class type inherits.</summary>
    private static readonly string[] _objectInheritedUids =
    [
        "M:System.Object.Equals(System.Object)",
        "M:System.Object.Equals(System.Object,System.Object)",
        "M:System.Object.GetHashCode",
        "M:System.Object.GetType",
        "M:System.Object.MemberwiseClone",
        "M:System.Object.ReferenceEquals(System.Object,System.Object)",
        "M:System.Object.ToString",
    ];

    /// <summary>Initializes a new instance of the <see cref="ZensicalCatalogIndexes"/> class from pre-built frozen lookups.</summary>
    /// <param name="derivedClasses">Reverse base-type lookup.</param>
    /// <param name="extensionMethods">Reverse first-parameter-type lookup over extension methods.</param>
    /// <param name="inheritedMembers">Per-type inherited member uid list (one base level + System.Object baseline).</param>
    private ZensicalCatalogIndexes(
        FrozenDictionary<string, ApiTypeReference[]> derivedClasses,
        FrozenDictionary<string, ApiMember[]> extensionMethods,
        FrozenDictionary<string, string[]> inheritedMembers)
    {
        DerivedClasses = derivedClasses;
        ExtensionMethods = extensionMethods;
        InheritedMembers = inheritedMembers;
    }

    /// <summary>Gets the empty index bundle — used by callers that don't supply a catalog.</summary>
    public static ZensicalCatalogIndexes Empty { get; } = new(
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
    /// Builds all three indexes in a single O(N) + O(N×Mext) sweep.
    /// Compiler-generated symbols are filtered out so display-class
    /// artefacts don't appear in any rollup.
    /// </summary>
    /// <param name="types">All types about to be rendered.</param>
    /// <returns>The frozen index bundle.</returns>
    public static ZensicalCatalogIndexes Build(ApiType[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (types is [])
        {
            return Empty;
        }

        var typesByUid = BuildTypesByUid(types);
        var derivedRaw = BuildDerivedRaw(types);
        var extensionsRaw = BuildExtensionsRaw(types);
        var inheritedRaw = BuildInheritedRaw(types, typesByUid);

        return new ZensicalCatalogIndexes(
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
            if (types[i] is ApiObjectType { Uid: [_, ..] uid } obj && obj.Name.AsSpan().IndexOfAny('<', '>') < 0)
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
            if (type.Name.AsSpan().IndexOfAny('<', '>') >= 0)
            {
                continue;
            }

            if (type.BaseType is not { Uid: [_, ..] baseUid })
            {
                continue;
            }

            var derivedRef = new ApiTypeReference(type.Name, type.Uid);
            if (!map.TryGetValue(baseUid, out var bucket))
            {
                bucket = [];
                map[baseUid] = bucket;
            }

            bucket.Add(derivedRef);
        }

        return map;
    }

    /// <summary>
    /// Single-pass build of the reverse extension-method lookup.
    /// </summary>
    /// <remarks>
    /// Best-effort: only extension methods whose first parameter has
    /// a concrete type uid are indexed. Generic-receiver extensions
    /// of the form <c>static void Foo&lt;T&gt;(this T self) where T : IBar</c>
    /// are skipped — docfx itself propagates these onto every type
    /// satisfying the constraint, but Zensical follows the same
    /// best-effort policy as the docfx emitter so the two outputs
    /// stay aligned.
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
                if (!member.IsExtension || member.Parameters is [] || member.Name.AsSpan().IndexOfAny('<', '>') >= 0)
                {
                    continue;
                }

                var extendedUid = member.Parameters[0].Type.Uid;
                if (extendedUid is not [_, ..])
                {
                    continue;
                }

                if (!map.TryGetValue(extendedUid, out var bucket))
                {
                    bucket = [];
                    map[extendedUid] = bucket;
                }

                bucket.Add(member);
            }
        }

        return map;
    }

    /// <summary>
    /// Builds inherited-member uid lists for every class / record type.
    /// Folds in members of the immediate base when it lives in our
    /// walked set; appends the universal <see cref="object"/> baseline.
    /// </summary>
    /// <param name="types">All types being emitted.</param>
    /// <param name="typesByUid">Pre-built lookup from <see cref="BuildTypesByUid"/>.</param>
    /// <returns>Mutable dictionary; converted to frozen form by the caller.</returns>
    internal static Dictionary<string, List<string>> BuildInheritedRaw(
        ApiType[] types,
        Dictionary<string, ApiObjectType> typesByUid)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is not ApiObjectType { Kind: ApiObjectKind.Class or ApiObjectKind.Record } cls)
            {
                continue;
            }

            if (cls.Name.AsSpan().IndexOfAny('<', '>') >= 0 || cls.Uid is not [_, ..] uid)
            {
                continue;
            }

            var inherited = new List<string>(_objectInheritedUids.Length + 8);

            if (cls.BaseType is { Uid: [_, ..] baseUid }
                && typesByUid.TryGetValue(baseUid, out var baseType))
            {
                AppendBaseMemberUids(inherited, baseType);
            }

            for (var k = 0; k < _objectInheritedUids.Length; k++)
            {
                inherited.Add(_objectInheritedUids[k]);
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
            if (member.Name.AsSpan().IndexOfAny('<', '>') >= 0 || member.Uid is not [_, ..] uid)
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
}
