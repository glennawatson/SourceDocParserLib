// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser.Merge;

/// <summary>
/// Responsible for merging types across multiple catalogs in a thread-safe manner.
/// Allows incremental addition of per-TFM catalogs and produces a canonical, merged
/// list of API types after processing. Once finalized, no further modifications are allowed.
/// </summary>
public sealed class StreamingTypeMerger
{
    /// <summary>Initial capacity for the per-UID bucket dictionary; matches <see cref="TypeMerger.Merge"/>.</summary>
    private const int InitialBucketCapacity = 4096;

    /// <summary>Initial capacity for each per-UID variant list.</summary>
    private const int InitialVariantCapacity = 4;

    /// <summary>Growth factor for the per-UID variant array.</summary>
    private const int GrowthFactor = 2;

    /// <summary>Per-UID variant buckets being built up.</summary>
    private readonly Dictionary<string, TypeVariant[]> _byUid =
        new(InitialBucketCapacity, StringComparer.Ordinal);

    /// <summary>Current count for each bucket in <see cref="_byUid"/>.</summary>
    private readonly Dictionary<string, int> _counts =
        new(InitialBucketCapacity, StringComparer.Ordinal);

    /// <summary>Lock guarding <see cref="_byUid"/> and <see cref="_counts"/> writes.</summary>
    private readonly Lock _lock = new();

    /// <summary>Set to true after <see cref="Build"/> runs so subsequent <see cref="Add"/>s throw.</summary>
    private bool _built;

    /// <summary>
    /// Adds <paramref name="catalog"/>'s types to the in-progress merge
    /// state. Safe to call from multiple workers concurrently.
    /// </summary>
    /// <param name="catalog">Per-TFM catalog to fold in.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="catalog"/> is null.</exception>
    /// <exception cref="InvalidOperationException">When called after <see cref="Build"/>.</exception>
    public void Add(ApiCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var tfm = Tfm.Tfm.Parse(catalog.Tfm);
        var types = catalog.Types;

        lock (_lock)
        {
            if (_built)
            {
                throw new InvalidOperationException("StreamingTypeMerger has already been built; further Add calls are not allowed.");
            }

            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                var uid = type.Uid;
                if (uid is [])
                {
                    continue;
                }

                if (!_byUid.TryGetValue(uid, out var bucket))
                {
                    bucket = new TypeVariant[InitialVariantCapacity];
                    _byUid[uid] = bucket;
                    _counts[uid] = 0;
                }

                var count = _counts[uid];
                if (count == bucket.Length)
                {
                    Array.Resize(ref bucket, bucket.Length * GrowthFactor);
                    _byUid[uid] = bucket;
                }

                bucket[count] = new(tfm, type);
                _counts[uid] = count + 1;
            }
        }
    }

    /// <summary>
    /// Produces the merged canonical list and seals the merger so further
    /// <see cref="Add"/> calls throw.
    /// </summary>
    /// <returns>A sorted array of canonical <see cref="ApiType"/>s.</returns>
    public ApiType[] Build()
    {
        lock (_lock)
        {
            _built = true;
        }

        var bucketCount = _byUid.Count;
        var merged = new ApiType[bucketCount];
        var keys = new string[bucketCount];
        _byUid.Keys.CopyTo(keys, 0);

        for (var i = 0; i < bucketCount; i++)
        {
            var uid = keys[i];
            var variants = _byUid[uid];
            var variantCount = _counts[uid];

            // Sort variants by descending TFM rank so the highest-priority TFM lands at index 0.
            Array.Sort(variants, 0, variantCount, Comparer<TypeVariant>.Create(static (a, b) => b.Tfm.Rank.CompareTo(a.Tfm.Rank)));

            var canonical = variants[0].Type;
            var appliesTo = new string[variantCount];
            for (var j = 0; j < variantCount; j++)
            {
                appliesTo[j] = variants[j].Tfm.Raw;
            }

            // Prefer a non-null SourceUrl from any variant.
            var sourceUrl = canonical.SourceUrl;
            if (sourceUrl is null)
            {
                for (var j = 1; j < variantCount; j++)
                {
                    var variantUrl = variants[j].Type.SourceUrl;
                    if (variantUrl is not { Length: > 0 })
                    {
                        continue;
                    }

                    sourceUrl = variantUrl;
                    break;
                }
            }

            merged[i] = canonical with { AppliesTo = appliesTo, SourceUrl = sourceUrl };
        }

        Array.Sort(merged, static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return merged;
    }

    /// <summary>
    /// One per-TFM occurrence of a type during the merge pass. Mirrors
    /// the private <c>TypeVariant</c> in <see cref="TypeMerger"/>.
    /// </summary>
    /// <param name="Tfm">Parsed TFM the variant came from.</param>
    /// <param name="Type">The per-TFM <see cref="ApiType"/>.</param>
    private readonly record struct TypeVariant(Tfm.Tfm Tfm, ApiType Type);
}
