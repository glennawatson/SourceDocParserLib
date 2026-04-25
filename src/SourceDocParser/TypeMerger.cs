// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Deduplicates <see cref="ApiType"/>s across multiple TFMs.
/// </summary>
/// <remarks>
/// Picks a canonical variant for each type based on TFM priority.
/// The newest TFM wins as the canonical variant, while the
/// <see cref="ApiType.AppliesTo"/> list aggregates all TFMs that
/// contain the type.
/// </remarks>
internal static class TypeMerger
{
    /// <summary>
    /// Gets the initial capacity for the per-UID bucket dictionary.
    /// </summary>
    private const int InitialBucketCapacity = 4096;

    /// <summary>
    /// Merges per-TFM catalogs into a single ordered list of types.
    /// </summary>
    /// <param name="catalogs">The collections of per-TFM catalogs to merge.</param>
    /// <returns>A sorted list of canonical <see cref="ApiType"/>s.</returns>
    public static List<ApiType> Merge(IReadOnlyCollection<ApiCatalog> catalogs)
    {
        var byUid = new Dictionary<string, List<(Tfm Tfm, ApiType Type)>>(InitialBucketCapacity, StringComparer.Ordinal);

        foreach (var catalog in catalogs)
        {
            var tfm = Tfm.Parse(catalog.Tfm);
            var types = catalog.Types;
            for (var i = 0; i < types.Count; i++)
            {
                var type = types[i];
                var uid = type.Uid;
                if (uid.Length == 0)
                {
                    continue;
                }

                if (!byUid.TryGetValue(uid, out var bucket))
                {
                    bucket = new List<(Tfm Tfm, ApiType Type)>(4);
                    byUid[uid] = bucket;
                }

                bucket.Add((tfm, type));
            }
        }

        var merged = new List<ApiType>(byUid.Count);
        foreach (var pair in byUid)
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
                    if (variantUrl is { Length: > 0 })
                    {
                        sourceUrl = variantUrl;
                        break;
                    }
                }
            }

            merged.Add(canonical with { AppliesTo = appliesTo, SourceUrl = sourceUrl });
        }

        merged.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return merged;
    }
}
