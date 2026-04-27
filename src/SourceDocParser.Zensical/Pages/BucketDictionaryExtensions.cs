// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Helpers for the <c>Dictionary&lt;TKey, List&lt;TVal&gt;&gt;</c>
/// pattern that <see cref="ZensicalCatalogIndexes"/> uses to assemble
/// each per-UID rollup. The "try-get / create-on-miss / add" sequence
/// appeared in three near-identical forms before extraction; folding
/// it into one place removes the bulk of the duplication SonarCloud
/// flagged on that file.
/// </summary>
internal static class BucketDictionaryExtensions
{
    /// <summary>
    /// Appends <paramref name="value"/> to the bucket keyed at
    /// <paramref name="key"/>, creating an empty bucket on first use.
    /// </summary>
    /// <typeparam name="TKey">Dictionary key type.</typeparam>
    /// <typeparam name="TVal">List element type.</typeparam>
    /// <param name="map">Per-key bucket dictionary.</param>
    /// <param name="key">Key to append under.</param>
    /// <param name="value">Element to append.</param>
    public static void AddToBucket<TKey, TVal>(this Dictionary<TKey, List<TVal>> map, TKey key, TVal value)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.TryGetValue(key, out var bucket))
        {
            bucket = [];
            map[key] = bucket;
        }

        bucket.Add(value);
    }
}
