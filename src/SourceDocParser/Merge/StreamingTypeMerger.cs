// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Streaming counterpart to <see cref="TypeMerger.Merge"/>. Accepts
/// <see cref="ApiCatalog"/>s one at a time as the parallel walk produces
/// them and emits the merged canonical list once <see cref="Build"/> is
/// called. Lets <see cref="MetadataExtractor.RunAsync"/> drop each
/// catalog reference as soon as it lands instead of keeping the whole
/// per-walk catalog set alive in a <c>ConcurrentBag</c> until the walk
/// phase finishes.
/// </summary>
/// <remarks>
/// Thread-safe: <see cref="Add"/> may be invoked concurrently by
/// multiple <c>Parallel.ForEachAsync</c> workers. <see cref="Build"/>
/// must be called exactly once after every <see cref="Add"/> has
/// returned; concurrent <see cref="Add"/>/<see cref="Build"/> is not
/// supported and will throw on misuse.
/// </remarks>
public sealed class StreamingTypeMerger
{
    /// <summary>Initial capacity for the per-UID bucket dictionary; matches <see cref="TypeMerger.Merge"/>.</summary>
    private const int InitialBucketCapacity = 4096;

    /// <summary>Per-UID variant buckets being built up.</summary>
    private readonly Dictionary<string, List<TypeVariant>> _byUid =
        new(InitialBucketCapacity, StringComparer.Ordinal);

    /// <summary>Lock guarding <see cref="_byUid"/> writes.</summary>
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

        var tfm = Tfm.Parse(catalog.Tfm);
        var types = catalog.Types;

        lock (_lock)
        {
            if (_built)
            {
                throw new InvalidOperationException("StreamingTypeMerger has already been built; further Add calls are not allowed.");
            }

            for (var i = 0; i < types.Count; i++)
            {
                var type = types[i];
                var uid = type.Uid;
                if (uid.Length == 0)
                {
                    continue;
                }

                if (!_byUid.TryGetValue(uid, out var bucket))
                {
                    bucket = new(4);
                    _byUid[uid] = bucket;
                }

                bucket.Add(new(tfm, type));
            }
        }
    }

    /// <summary>
    /// Produces the merged canonical list and seals the merger so further
    /// <see cref="Add"/> calls throw.
    /// </summary>
    /// <returns>A sorted list of canonical <see cref="ApiType"/>s.</returns>
    public List<ApiType> Build()
    {
        lock (_lock)
        {
            _built = true;
        }

        var merged = new List<ApiType>(_byUid.Count);
        foreach (var pair in _byUid)
        {
            var variants = pair.Value;

            // Sort variants by descending TFM rank so the highest-priority TFM lands at index 0.
            variants.Sort(static (a, b) => b.Tfm.Rank.CompareTo(a.Tfm.Rank));

            var canonical = variants[0].Type;
            var appliesTo = new List<string>(variants.Count);
            for (var i = 0; i < variants.Count; i++)
            {
                appliesTo.Add(variants[i].Tfm.Raw);
            }

            // Prefer a non-null SourceUrl from any variant.
            var sourceUrl = canonical.SourceUrl;
            if (sourceUrl is null)
            {
                for (var i = 1; i < variants.Count; i++)
                {
                    var variantUrl = variants[i].Type.SourceUrl;
                    if (variantUrl is not { Length: > 0 })
                    {
                        continue;
                    }

                    sourceUrl = variantUrl;
                    break;
                }
            }

            merged.Add(canonical with { AppliesTo = appliesTo, SourceUrl = sourceUrl });
        }

        merged.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return merged;
    }

    /// <summary>
    /// One per-TFM occurrence of a type during the merge pass. Mirrors
    /// the private <c>TypeVariant</c> in <see cref="TypeMerger"/>.
    /// </summary>
    /// <param name="Tfm">Parsed TFM the variant came from.</param>
    /// <param name="Type">The per-TFM <see cref="ApiType"/>.</param>
    private readonly record struct TypeVariant(Tfm Tfm, ApiType Type);
}
