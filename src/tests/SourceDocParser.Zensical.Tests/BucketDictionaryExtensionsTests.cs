// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="BucketDictionaryExtensions.AddToBucket"/> -- the
/// "try-get / create-on-miss / append" helper that the catalog
/// indexes use to assemble per-UID lookups. The shape is small but
/// it sits on a hot per-type loop so the contract pins matter:
/// first call creates a bucket, subsequent calls reuse it,
/// equal keys collapse, and a null map argument is rejected up
/// front.
/// </summary>
public class BucketDictionaryExtensionsTests
{
    /// <summary>First call into an empty key creates the bucket and appends the value.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddToBucketCreatesBucketOnFirstCall()
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        map.AddToBucket("k", 1);

        await Assert.That(map.ContainsKey("k")).IsTrue();
        await Assert.That(map["k"]).IsEquivalentTo((int[])[1]);
    }

    /// <summary>Subsequent calls into the same key append to the existing bucket.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddToBucketAppendsOnRepeatedKey()
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        map.AddToBucket("k", 1);
        map.AddToBucket("k", 2);
        map.AddToBucket("k", 3);

        await Assert.That(map["k"]).IsEquivalentTo((int[])[1, 2, 3]);
        await Assert.That(map.Count).IsEqualTo(1);
    }

    /// <summary>Distinct keys produce distinct buckets.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddToBucketKeepsDistinctKeysSeparate()
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        map.AddToBucket("a", 1);
        map.AddToBucket("b", 2);
        map.AddToBucket("a", 3);

        await Assert.That(map["a"]).IsEquivalentTo((int[])[1, 3]);
        await Assert.That(map["b"]).IsEquivalentTo((int[])[2]);
    }

    /// <summary>Reference values are stored by reference -- the helper is a thin shim, no copy semantics.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddToBucketStoresReferenceValuesByReference()
    {
        var map = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var value = new object();

        map.AddToBucket("k", value);

        await Assert.That(map["k"][0]).IsSameReferenceAs(value);
    }

    /// <summary>A null map argument throws -- defensive against accidental misuse on a hot caller.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddToBucketRejectsNullMap()
    {
        Dictionary<string, List<int>>? map = null;

        await Assert.That(() => map!.AddToBucket("k", 1)).Throws<ArgumentNullException>();
    }
}
