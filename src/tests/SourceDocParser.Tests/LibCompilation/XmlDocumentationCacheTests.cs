// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests.LibCompilation;

/// <summary>Verifies documentation reuse without sharing versions or retaining expired extraction state.</summary>
public sealed class XmlDocumentationCacheTests
{
    /// <summary>Fixture package identity.</summary>
    private const string Package = "package";

    /// <summary>Fixture documentation contents.</summary>
    private const string Summary = "Summary";

    /// <summary>Budget sufficient for two small documents.</summary>
    private const long TwoEntryBudget = 150_000;

    /// <summary>Timestamp change larger than filesystem timestamp precision.</summary>
    private const int TimestampOffsetSeconds = 2;

    /// <summary>Identical assets reuse documentation while package versions remain separate.</summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task ReusesOnlyTheSameAsset()
    {
        using var directory = new TempDirectory();
        using var cache = new XmlDocumentationCache();
        using var otherRun = new XmlDocumentationCache();
        var firstPath = await WriteAsync(directory.Path, "1.0.0", "First version");
        var secondPath = await WriteAsync(directory.Path, "2.0.0", "Second version");
        var first = cache.Get(firstPath, NullLogger.Instance);

        await Assert.That(first).IsNotNull();
        await Assert.That(cache.Get(firstPath, NullLogger.Instance)).IsSameReferenceAs(first);
        await Assert.That(ReferenceEquals(first, cache.Get(secondPath, NullLogger.Instance))).IsFalse();
        await Assert.That(ReferenceEquals(first, otherRun.Get(firstPath, NullLogger.Instance))).IsFalse();
    }

    /// <summary>Either modification time or file length invalidates a retained provider.</summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task InvalidatesChangedFiles()
    {
        using var directory = new TempDirectory();
        using var cache = new XmlDocumentationCache();
        var path = await WriteAsync(directory.Path, Package, "First");
        var initial = cache.Get(path, NullLogger.Instance);
        var xmlPath = Path.ChangeExtension(path, ".xml");
        var stamp = File.GetLastWriteTimeUtc(xmlPath).AddSeconds(TimestampOffsetSeconds);
        _ = await WriteAsync(directory.Path, Package, "Other");
        File.SetLastWriteTimeUtc(xmlPath, stamp);
        var changedTime = cache.Get(path, NullLogger.Instance);
        _ = await WriteAsync(directory.Path, Package, "Longer documentation");
        File.SetLastWriteTimeUtc(xmlPath, stamp);

        await Assert.That(ReferenceEquals(initial, changedTime)).IsFalse();
        await Assert.That(ReferenceEquals(changedTime, cache.Get(path, NullLogger.Instance))).IsFalse();
    }

    /// <summary>The least recently used asset is evicted when the budget holds only two entries.</summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task EvictsLeastRecentlyUsedDocumentation()
    {
        using var directory = new TempDirectory();
        using var cache = new XmlDocumentationCache(TwoEntryBudget);
        var a = await WriteAsync(directory.Path, "a", Summary);
        var b = await WriteAsync(directory.Path, "b", Summary);
        var c = await WriteAsync(directory.Path, "c", Summary);
        var first = cache.Get(a, NullLogger.Instance);
        var second = cache.Get(b, NullLogger.Instance);
        _ = cache.Get(a, NullLogger.Instance);
        _ = cache.Get(c, NullLogger.Instance);

        await Assert.That(cache.Get(a, NullLogger.Instance)).IsSameReferenceAs(first);
        await Assert.That(ReferenceEquals(second, cache.Get(b, NullLogger.Instance))).IsFalse();
    }

    /// <summary>Oversized documents remain available without being retained.</summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task BypassesOversizedAndMissingFiles()
    {
        using var directory = new TempDirectory();
        using var cache = new XmlDocumentationCache(1);
        var path = Path.Combine(directory.Path, Package, "Library.dll");
        await Assert.That(cache.Get(path, NullLogger.Instance)).IsNull();
        _ = await WriteAsync(directory.Path, Package, Summary);
        var first = cache.Get(path, NullLogger.Instance);

        await Assert.That(first).IsNotNull();
        await Assert.That(ReferenceEquals(first, cache.Get(path, NullLogger.Instance))).IsFalse();
    }

    /// <summary>Nonpositive budgets are invalid.</summary>
    /// <param name="budget">Rejected budget.</param>
    /// <returns>The test task.</returns>
    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    public async Task RejectsInvalidBudgets(long budget) =>
        await Assert.That(() => new XmlDocumentationCache(budget)).Throws<ArgumentOutOfRangeException>();

    /// <summary>Disposal during a load prevents that load from refilling the cache.</summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task DisposalDuringLoadRetiresTheCache()
    {
        using var directory = new TempDirectory();
        using var cache = new XmlDocumentationCache();
        var path = await WriteAsync(directory.Path, Package, Summary);
        var provider = cache.Get(path, new DisposingLogger(cache));

        await Assert.That(provider).IsNotNull();
        await Assert.That(() => cache.Get(path, NullLogger.Instance)).Throws<ObjectDisposedException>();
    }

    /// <summary>Concurrent lookups remain valid and leave a reusable provider.</summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task SupportsConcurrentLookups()
    {
        using var directory = new TempDirectory();
        using var cache = new XmlDocumentationCache();
        var path = await WriteAsync(directory.Path, Package, Summary);
        var providers = new DocumentationProvider?[32];
        _ = Parallel.For(0, providers.Length, i => providers[i] = cache.Get(path, NullLogger.Instance));

        await Assert.That(Array.TrueForAll(providers, static provider => provider is not null)).IsTrue();
        await Assert.That(cache.Get(path, NullLogger.Instance)).IsSameReferenceAs(cache.Get(path, NullLogger.Instance));
    }

    /// <summary>Writes documentation beside a package-shaped assembly path.</summary>
    /// <param name="directory">Fixture directory.</param>
    /// <param name="version">Package version or asset identity.</param>
    /// <param name="summary">Documentation contents.</param>
    /// <returns>The assembly path.</returns>
    private static async Task<string> WriteAsync(string directory, string version, string summary)
    {
        var path = Path.Combine(directory, version, "Library.dll");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".xml"), $"<doc><members><member name=\"T:Library.Type\"><summary>{summary}</summary></member></members></doc>");
        return path;
    }

    /// <summary>Ends extraction when a documentation load completes.</summary>
    /// <param name="cache">Cache to retire.</param>
    private sealed class DisposingLogger(XmlDocumentationCache cache) : ILogger
    {
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => cache.Dispose();
    }
}
