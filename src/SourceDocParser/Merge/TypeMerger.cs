// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.Merge;

/// <summary>
/// Deduplicates <see cref="ApiType"/>s across multiple TFMs.
/// </summary>
/// <remarks>
/// Picks a canonical variant for each type based on TFM priority.
/// The newest TFM wins as the canonical variant, while the
/// <see cref="ApiType.AppliesTo"/> list aggregates all TFMs that
/// contain the type.
/// </remarks>
public static class TypeMerger
{
    /// <summary>
    /// Gets the initial capacity for the per-UID bucket dictionary.
    /// </summary>
    private const int InitialBucketCapacity = 4096;

    /// <summary>
    /// Gets the initial capacity for each per-UID variant bucket.
    /// </summary>
    private const int InitialVariantCapacity = 4;

    /// <summary>
    /// Gets the growth factor used when a variant bucket fills up.
    /// </summary>
    private const int GrowthFactor = 2;

    /// <summary>
    /// Merges per-TFM catalogs into a single ordered list of types.
    /// </summary>
    /// <param name="catalogs">The collections of per-TFM catalogs to merge.</param>
    /// <returns>A sorted array of canonical <see cref="ApiType"/>s.</returns>
    public static ApiType[] Merge(List<ApiCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        var byUid = new Dictionary<string, Bucket>(InitialBucketCapacity, StringComparer.Ordinal);

        for (var catalogIndex = 0; catalogIndex < catalogs.Count; catalogIndex++)
        {
            AddCatalogVariants(catalogs[catalogIndex], byUid);
        }

        var merged = BuildMergedTypes(byUid);
        Array.Sort(merged, static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return merged;
    }

    /// <summary>
    /// Adds one catalog's types into the per-UID merge buckets.
    /// </summary>
    /// <param name="catalog">The catalog to fold into the merge state.</param>
    /// <param name="byUid">Bucket storage keyed by UID.</param>
    internal static void AddCatalogVariants(
        ApiCatalog catalog,
        Dictionary<string, Bucket> byUid)
    {
        var tfm = Tfm.Tfm.Parse(catalog.Tfm);
        var types = catalog.Types;

        for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
        {
            var type = types[typeIndex];
            var uid = type.Uid;
            if (uid is [])
            {
                continue;
            }

            AddVariant(uid, tfm, type, byUid);
        }
    }

    /// <summary>
    /// Appends one type variant to its UID bucket, growing the bucket when needed.
    /// </summary>
    /// <param name="uid">UID key for the bucket.</param>
    /// <param name="tfm">TFM associated with the type variant.</param>
    /// <param name="type">The type variant to append.</param>
    /// <param name="byUid">Bucket storage keyed by UID.</param>
    internal static void AddVariant(
        string uid,
        Tfm.Tfm tfm,
        ApiType type,
        Dictionary<string, Bucket> byUid)
    {
        if (!byUid.TryGetValue(uid, out var bucket))
        {
            bucket = new(InitialVariantCapacity);
            byUid[uid] = bucket;
        }

        var items = bucket.Items;
        if (bucket.Count == items.Length)
        {
            Array.Resize(ref items, items.Length * GrowthFactor);
            bucket.Items = items;
        }

        items[bucket.Count] = new(tfm, type);
        bucket.Count++;
    }

    /// <summary>
    /// Builds the canonical merged types from the per-UID buckets.
    /// </summary>
    /// <param name="byUid">Bucket storage keyed by UID.</param>
    /// <returns>The unsorted merged type array.</returns>
    internal static ApiType[] BuildMergedTypes(Dictionary<string, Bucket> byUid)
    {
        var merged = new ApiType[byUid.Count];
        var i = 0;
        foreach (var bucket in byUid.Values)
        {
            merged[i++] = BuildCanonicalType(bucket);
        }

        return merged;
    }

    /// <summary>
    /// Builds the canonical merged type for a single UID bucket.
    /// </summary>
    /// <param name="bucket">All variants for one UID.</param>
    /// <returns>The canonical merged type.</returns>
    internal static ApiType BuildCanonicalType(Bucket bucket)
    {
        SortVariantsByRank(bucket.Items, bucket.Count);

        var canonical = bucket.Items[0].Type;
        return canonical with
        {
            AppliesTo = BuildAppliesTo(bucket.Items, bucket.Count),
            SourceUrl = ResolveSourceUrl(canonical.SourceUrl, bucket.Items, bucket.Count),
        };
    }

    /// <summary>
    /// Sorts a variant bucket by descending TFM rank.
    /// </summary>
    /// <param name="variants">Variant bucket to sort.</param>
    /// <param name="variantCount">How many entries in <paramref name="variants"/> are populated.</param>
    internal static void SortVariantsByRank(TypeVariant[] variants, int variantCount)
    {
        if (variantCount is <= 1)
        {
            return;
        }

        Array.Sort(variants, 0, variantCount, TypeVariantRankComparer.Instance);
    }

    /// <summary>
    /// Builds the AppliesTo array from the populated variants in a bucket.
    /// </summary>
    /// <param name="variants">Variant bucket for one UID.</param>
    /// <param name="variantCount">How many entries in <paramref name="variants"/> are populated.</param>
    /// <returns>The AppliesTo array in descending TFM rank order.</returns>
    internal static string[] BuildAppliesTo(TypeVariant[] variants, int variantCount)
    {
        var appliesTo = new string[variantCount];
        for (var variantIndex = 0; variantIndex < variantCount; variantIndex++)
        {
            appliesTo[variantIndex] = variants[variantIndex].Tfm.Raw;
        }

        return appliesTo;
    }

    /// <summary>
    /// Resolves the canonical source URL, preferring the canonical variant's value and then
    /// the first non-empty URL from the remaining variants when the canonical URL is null.
    /// </summary>
    /// <param name="sourceUrl">Source URL from the canonical variant.</param>
    /// <param name="variants">Variant bucket for one UID.</param>
    /// <param name="variantCount">How many entries in <paramref name="variants"/> are populated.</param>
    /// <returns>The chosen source URL, if any.</returns>
    internal static string? ResolveSourceUrl(string? sourceUrl, TypeVariant[] variants, int variantCount)
    {
        if (sourceUrl is not null)
        {
            return sourceUrl;
        }

        for (var variantIndex = 1; variantIndex < variantCount; variantIndex++)
        {
            var variantUrl = variants[variantIndex].Type.SourceUrl;
            if (variantUrl is not { Length: > 0 })
            {
                continue;
            }

            return variantUrl;
        }

        return null;
    }

    /// <summary>
    /// One per-TFM occurrence of a type during the merge pass -- paired
    /// so the bucket dictionary holds a stable, named tuple instead of
    /// an anonymous <c>(Tfm, ApiType)</c>.
    /// </summary>
    /// <param name="Tfm">Parsed TFM the variant came from.</param>
    /// <param name="Type">The per-TFM <see cref="ApiType"/>.</param>
    internal readonly record struct TypeVariant(Tfm.Tfm Tfm, ApiType Type);

    /// <summary>
    /// Per-UID growable bucket: a class wrapper so the dictionary stores
    /// one heap object per UID instead of needing a parallel
    /// <c>Dictionary&lt;string, int></c> to track each bucket's count.
    /// </summary>
    internal sealed class Bucket
    {
        /// <summary>Initializes a new instance of the <see cref="Bucket"/> class.</summary>
        /// <param name="initialCapacity">Initial size of <see cref="Items"/>.</param>
        public Bucket(int initialCapacity)
        {
            Items = new TypeVariant[initialCapacity];
        }

        /// <summary>Gets or sets the variant storage; resized in place as the bucket fills.</summary>
        public TypeVariant[] Items { get; set; }

        /// <summary>Gets or sets the populated entry count in <see cref="Items"/>.</summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// Descending comparer for per-UID type variants by TFM rank.
    /// </summary>
    internal sealed class TypeVariantRankComparer : IComparer<TypeVariant>
    {
        /// <summary>
        /// Gets the shared comparer instance.
        /// </summary>
        public static TypeVariantRankComparer Instance { get; } = new();

        /// <inheritdoc />
        public int Compare(TypeVariant x, TypeVariant y) => y.Tfm.Rank.CompareTo(x.Tfm.Rank);
    }
}
