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
public static class TypeMerger
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
    public static List<ApiType> Merge(List<ApiCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        var byUid = new Dictionary<string, List<TypeVariant>>(InitialBucketCapacity, StringComparer.Ordinal);

        foreach (var (tfmString, types) in catalogs)
        {
            var tfm = Tfm.Parse(tfmString);
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
                    bucket = new(4);
                    byUid[uid] = bucket;
                }

                bucket.Add(new(tfm, type));
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
    /// One per-TFM occurrence of a type during the merge pass — paired
    /// so the bucket dictionary holds a stable, named tuple instead of
    /// an anonymous <c>(Tfm, ApiType)</c>.
    /// </summary>
    /// <param name="Tfm">Parsed TFM the variant came from.</param>
    /// <param name="Type">The per-TFM <see cref="ApiType"/>.</param>
    private readonly record struct TypeVariant(Tfm Tfm, ApiType Type);
}
