// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using SourceDocParser.Merge;
using SourceDocParser.Model;

namespace SourceDocParser.Tests;

/// <summary>
/// Unit tests for the <c>StreamingTypeMerger</c> class. These tests verify the behavior of the merging process
/// for types from multiple target frameworks and ensure the correctness of the resulting merged type collection.
/// </summary>
public class StreamingTypeMergerTests
{
    /// <summary>Fixture value for Net100.</summary>
    private const string Net100 = "net10.0";

    /// <summary>Fixture value for FooBar.</summary>
    private const string FooBar = "Foo.Bar";

    /// <summary>A single catalog with a single type round-trips through the merger with a one-element AppliesTo list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildReturnsSingleVariantWhenOnlyOneTfmContributes()
    {
        var merger = new StreamingTypeMerger();
        merger.Add(new(Net100, [BuildType(FooBar)]));

        var merged = merger.Build();

        await Assert.That(merged.Length).IsEqualTo(1);
        await Assert.That(merged[0].FullName).IsEqualTo(FooBar);
        await Assert.That(merged[0].AppliesTo).IsEquivalentTo((List<string>)[Net100]);
    }

    /// <summary>A type present in two TFMs collapses to one canonical entry whose <c>AppliesTo</c> aggregates both TFMs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildAggregatesAppliesToAcrossTfms()
    {
        var merger = new StreamingTypeMerger();
        merger.Add(new("net9.0", [BuildType(FooBar)]));
        merger.Add(new(Net100, [BuildType(FooBar)]));

        var merged = merger.Build();

        await Assert.That(merged.Length).IsEqualTo(1);
        List<string> sorted = [.. merged[0].AppliesTo];
        sorted.Sort(StringComparer.Ordinal);
        await Assert.That(sorted).IsEquivalentTo((List<string>)[Net100, "net9.0"]);
    }

    /// <summary>Types with empty UID are skipped (UID-keyed merge can't bucket them).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildSkipsTypesWithEmptyUid()
    {
        var merger = new StreamingTypeMerger();
        merger.Add(new(Net100, [BuildType(FooBar), BuildType(string.Empty)]));

        var merged = merger.Build();

        await Assert.That(merged.Length).IsEqualTo(1);
        await Assert.That(merged[0].FullName).IsEqualTo(FooBar);
    }

    /// <summary>Calling <see cref="StreamingTypeMerger.Add"/> after <see cref="StreamingTypeMerger.Build"/> throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddAfterBuildThrows()
    {
        var merger = new StreamingTypeMerger();
        _ = merger.Build();

        await Assert.That(() => merger.Add(new(Net100, [BuildType(FooBar)])))
            .Throws<InvalidOperationException>();
    }

    /// <summary>Null catalog throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddValidatesArguments()
    {
        var merger = new StreamingTypeMerger();
        await Assert.That(() => merger.Add(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Concurrent <see cref="StreamingTypeMerger.Add"/> from multiple
    /// workers produces the same merged set as a sequential add -- proves
    /// the lock-guarded write path is correct under contention.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddIsThreadSafeUnderConcurrentWriters()
    {
        var merger = new StreamingTypeMerger();
        const int workerCount = 16;
        const int typesPerWorker = 50;

        _ = Parallel.For(0, workerCount, w =>
        {
            List<ApiType> batch = [];
            for (var i = 0; i < typesPerWorker; i++)
            {
                batch.Add(BuildType($"Worker{w}.Type{i:D3}"));
            }

            merger.Add(new(Net100, [.. batch]));
        });

        var merged = merger.Build();
        await Assert.That(merged.Length).IsEqualTo(workerCount * typesPerWorker);
    }

    /// <summary>Builds a minimal <see cref="ApiType"/> with the supplied UID (also used as Name + FullName so sort order is deterministic).</summary>
    /// <param name="uid">UID/Name/FullName for the synthetic type.</param>
    /// <returns>The constructed type.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ApiObjectType BuildType(string uid) => TestHelpers.TestData.ObjectType(uid);
}
