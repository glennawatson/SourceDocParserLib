// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Merge;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="TypeMerger"/> — the dedup pass that collapses
/// per-TFM catalogs into one canonical view per type UID.
/// </summary>
public class TypeMergerTests
{
    /// <summary>
    /// A type that appears in multiple TFMs collapses to one canonical entry.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MergeDeduplicatesByUid()
    {
        var net8 = TestData.Catalog("net8.0", TestData.Type("Foo"));
        var net9 = TestData.Catalog("net9.0", TestData.Type("Foo"));
        var net10 = TestData.Catalog("net10.0", TestData.Type("Foo"));

        var merged = TypeMerger.Merge([net8, net9, net10]);

        await Assert.That(merged.Length).IsEqualTo(1);
        await Assert.That(merged[0].Uid).IsEqualTo("Foo");
    }

    /// <summary>
    /// AppliesTo lists every TFM that contained the type, in highest-rank order.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MergePopulatesAppliesToWithEveryTfm()
    {
        var net8 = TestData.Catalog("net8.0", TestData.Type("Foo"));
        var net10 = TestData.Catalog("net10.0", TestData.Type("Foo"));

        var merged = TypeMerger.Merge([net8, net10]);

        await Assert.That(merged.Single().AppliesTo).Contains("net8.0");
        await Assert.That(merged.Single().AppliesTo).Contains("net10.0");

        // Highest rank lands first.
        await Assert.That(merged.Single().AppliesTo[0]).IsEqualTo("net10.0");
    }

    /// <summary>
    /// SourceUrl from a non-canonical variant fills in when the canonical one is null.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MergePicksNonNullSourceUrlFromAnyVariant()
    {
        var net10 = TestData.Catalog("net10.0", TestData.Type("Foo", "Test", null));
        var net8 = TestData.Catalog("net8.0", TestData.Type("Foo", "Test", "https://example.test/Foo.cs#L1"));

        var merged = TypeMerger.Merge([net10, net8]);

        await Assert.That(merged.Single().SourceUrl).IsEqualTo("https://example.test/Foo.cs#L1");
    }

    /// <summary>
    /// Distinct UIDs survive as distinct merged entries.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MergeKeepsDistinctTypes()
    {
        var net10 = TestData.Catalog("net10.0", TestData.Type("Foo"), TestData.Type("Bar"));

        var merged = TypeMerger.Merge([net10]);

        await Assert.That(merged.Length).IsEqualTo(2);
    }

    /// <summary>
    /// Merge results are sorted by FullName for deterministic page output.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MergeSortsByFullName()
    {
        var net10 = TestData.Catalog(
            "net10.0",
            TestData.Type("Zebra"),
            TestData.Type("Apple"),
            TestData.Type("Mango"));

        var merged = TypeMerger.Merge([net10]);

        await Assert.That(merged[0].FullName).IsEqualTo("Apple");
        await Assert.That(merged[1].FullName).IsEqualTo("Mango");
        await Assert.That(merged[2].FullName).IsEqualTo("Zebra");
    }

    /// <summary>
    /// Types with empty UIDs are skipped (they happen on broken metadata).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MergeIgnoresEmptyUids()
    {
        var net10 = TestData.Catalog(
            "net10.0",
            TestData.Type(string.Empty),
            TestData.Type("Real"));

        var merged = TypeMerger.Merge([net10]);

        await Assert.That(merged.Length).IsEqualTo(1);
        await Assert.That(merged[0].Uid).IsEqualTo("Real");
    }
}
