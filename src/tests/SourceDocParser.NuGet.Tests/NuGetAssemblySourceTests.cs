// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;

using ProbedTfm = SourceDocParser.NuGet.Infrastructure.NuGetAssemblySource.ProbedTfm;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Constructor-level coverage for <see cref="NuGetAssemblySource"/>.
/// Heavier scenarios that touch nuget.org live in
/// <c>SourceDocParser.IntegrationTests</c>.
/// </summary>
public class NuGetAssemblySourceTests
{
    /// <summary>Constructing with a null root directory throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsNullRootDirectory()
    {
        var apiPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "api");

        await Assert.That(Act).Throws<ArgumentNullException>();

        void Act() => _ = new NuGetAssemblySource(rootDirectory: null!, apiPath: apiPath);
    }

    /// <summary>Constructing with a null api path throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsNullApiPath()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "repo");

        await Assert.That(Act).Throws<ArgumentNullException>();

        void Act() => _ = new NuGetAssemblySource(rootDirectory: rootDirectory, apiPath: null!);
    }

    /// <summary>
    /// Discovery excludes package DLLs whose simple name is already supplied by
    /// the matched refs/ TFM, so the parser walks the implementation assembly set
    /// without duplicating co-located reference shims.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverAsyncSkipsLibAssembliesShadowedByRefs()
    {
        using var root = new TempDirectory();
        using var api = new TempDirectory();
        var libTfmDir = Path.Combine(api.Path, "lib", "net10.0");
        var refsTfmDir = Path.Combine(api.Path, "refs", "net10.0");
        Directory.CreateDirectory(libTfmDir);
        Directory.CreateDirectory(refsTfmDir);

        await File.WriteAllBytesAsync(Path.Combine(libTfmDir, "Package.dll"), []);
        await File.WriteAllBytesAsync(Path.Combine(libTfmDir, "Shared.dll"), []);
        await File.WriteAllBytesAsync(Path.Combine(refsTfmDir, "Shared.dll"), []);

        var source = new NuGetAssemblySource(root.Path, api.Path, logger: null, fetcher: new NoOpFetcher());
        List<AssemblyGroup> groups = [];
        await foreach (var group in source.DiscoverAsync())
        {
            groups.Add(group);
        }

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo("net10.0");
        await Assert.That(groups[0].AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(Path.GetFileName(groups[0].AssemblyPaths[0])).IsEqualTo("Package.dll");
    }

    /// <summary>
    /// <see cref="NuGetAssemblySource.DiscoverAsync()"/> throws
    /// <see cref="DirectoryNotFoundException"/> when the fetcher
    /// completes without producing a <c>lib/</c> tree -- the absent
    /// directory is the signal that the apiPath is uninitialised, and
    /// silently yielding nothing would mask the misconfiguration.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverAsyncThrowsWhenLibDirectoryMissing()
    {
        using var root = new TempDirectory();
        using var api = new TempDirectory();

        var source = new NuGetAssemblySource(root.Path, api.Path, logger: null, fetcher: new NoOpFetcher());

        await Assert.That(Act).Throws<DirectoryNotFoundException>();

        async Task Act()
        {
            await foreach (var group in source.DiscoverAsync())
            {
                _ = group;
            }
        }
    }

    /// <summary>
    /// Cancellation requested before the first yield surfaces as an
    /// <see cref="OperationCanceledException"/> -- the iterator checks
    /// the token after the fetcher returns and again between yields.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverAsyncHonoursCancellationAfterFetch()
    {
        using var root = new TempDirectory();
        using var api = new TempDirectory();
        var libTfmDir = Path.Combine(api.Path, "lib", "net10.0");
        Directory.CreateDirectory(libTfmDir);
        await File.WriteAllBytesAsync(Path.Combine(libTfmDir, "Package.dll"), []);

        var source = new NuGetAssemblySource(root.Path, api.Path, logger: null, fetcher: new NoOpFetcher());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(Act).Throws<OperationCanceledException>();

        async Task Act()
        {
            await foreach (var group in source.DiscoverAsync(cts.Token))
            {
                _ = group;
            }
        }
    }

    /// <summary>
    /// The single-arg <see cref="NuGetAssemblySource.DiscoverAsync()"/>
    /// overload delegates to the cancellable variant -- exercise it so
    /// the public no-token entry point is covered.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverAsyncNoTokenOverloadYieldsGroups()
    {
        using var root = new TempDirectory();
        using var api = new TempDirectory();
        var libTfmDir = Path.Combine(api.Path, "lib", "net10.0");
        Directory.CreateDirectory(libTfmDir);
        await File.WriteAllBytesAsync(Path.Combine(libTfmDir, "Package.dll"), []);

        var source = new NuGetAssemblySource(root.Path, api.Path, logger: null, fetcher: new NoOpFetcher());
        List<AssemblyGroup> groups = [];
        await foreach (var group in source.DiscoverAsync())
        {
            groups.Add(group);
        }

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo("net10.0");
    }

    /// <summary>
    /// <see cref="NuGetAssemblySource.SelectCanonicalsAndBroadcasts"/>
    /// short-circuits on the empty input -- no canonical scratch
    /// allocation happens, the result is the shared empty array.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCanonicalsAndBroadcastsReturnsEmptyForEmptyInput()
    {
        var groups = NuGetAssemblySource.SelectCanonicalsAndBroadcasts([]);

        await Assert.That(groups.Length).IsEqualTo(0);
    }

    /// <summary>
    /// Null input is rejected with the standard guard so callers fail
    /// loudly rather than NREing inside the ranking helper.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCanonicalsAndBroadcastsRejectsNull()
    {
        await Assert.That(() => NuGetAssemblySource.SelectCanonicalsAndBroadcasts(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// A lone probe with no subset peers becomes a single canonical
    /// with no broadcast targets -- exercises the no-match branch in
    /// the canonical-assignment loop.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCanonicalsAndBroadcastsPromotesSingleProbeToCanonical()
    {
        var probe = new ProbedTfm(
            Tfm: "net10.0",
            Dlls: ["Package.dll"],
            Fallback: new(StringComparer.OrdinalIgnoreCase),
            Uids: ["T:Sample.Type"],
            Rank: 100);

        var groups = NuGetAssemblySource.SelectCanonicalsAndBroadcasts([probe]);

        await Assert.That(groups.Length).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo("net10.0");
        await Assert.That(groups[0].BroadcastTfms.Length).IsEqualTo(0);
    }

    /// <summary>
    /// Lower-ranked TFMs whose public-type UID set is a subset of a
    /// higher-ranked canonical collapse onto the canonical's
    /// <see cref="AssemblyGroup.BroadcastTfms"/> -- the merger then
    /// stamps the walked types with every broadcast TFM. Five extra
    /// subsets force <see cref="NuGetAssemblySource"/>'s broadcast
    /// scratch array past its initial four-slot capacity, exercising
    /// the resize path.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCanonicalsAndBroadcastsGrowsBroadcastSlotsOnFifthSubset()
    {
        var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> canonicalUids = ["T:Sample.A", "T:Sample.B"];
        HashSet<string> subsetUids = ["T:Sample.A"];
        var probes = new[]
        {
            new ProbedTfm("net10.0", ["A.dll"], fallback, canonicalUids, Rank: 100),
            new ProbedTfm("net9.0", ["A.dll"], fallback, subsetUids, Rank: 90),
            new ProbedTfm("net8.0", ["A.dll"], fallback, subsetUids, Rank: 80),
            new ProbedTfm("net7.0", ["A.dll"], fallback, subsetUids, Rank: 70),
            new ProbedTfm("net6.0", ["A.dll"], fallback, subsetUids, Rank: 60),
            new ProbedTfm("netstandard2.1", ["A.dll"], fallback, subsetUids, Rank: 50),
        };

        var groups = NuGetAssemblySource.SelectCanonicalsAndBroadcasts(probes);

        await Assert.That(groups.Length).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo("net10.0");
        await Assert.That(groups[0].BroadcastTfms.Length).IsEqualTo(5);
        await Assert.That(groups[0].BroadcastTfms).Contains("net9.0");
        await Assert.That(groups[0].BroadcastTfms).Contains("netstandard2.1");
    }

    /// <summary>
    /// Exactly four subset peers fill the broadcast scratch array's
    /// initial capacity without triggering the resize -- pins the
    /// "exact fit, return scratch verbatim" branch in
    /// <c>MaterialiseGroups</c> where the trim allocation is skipped.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCanonicalsAndBroadcastsKeepsBroadcastScratchOnExactFit()
    {
        var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> canonicalUids = ["T:Sample.A", "T:Sample.B"];
        HashSet<string> subsetUids = ["T:Sample.A"];
        var probes = new[]
        {
            new ProbedTfm("net10.0", ["A.dll"], fallback, canonicalUids, Rank: 100),
            new ProbedTfm("net9.0", ["A.dll"], fallback, subsetUids, Rank: 90),
            new ProbedTfm("net8.0", ["A.dll"], fallback, subsetUids, Rank: 80),
            new ProbedTfm("net7.0", ["A.dll"], fallback, subsetUids, Rank: 70),
            new ProbedTfm("net6.0", ["A.dll"], fallback, subsetUids, Rank: 60),
        };

        var groups = NuGetAssemblySource.SelectCanonicalsAndBroadcasts(probes);

        await Assert.That(groups.Length).IsEqualTo(1);
        await Assert.That(groups[0].BroadcastTfms.Length).IsEqualTo(4);
    }

    /// <summary>
    /// Two probes with identical <see cref="ProbedTfm.Rank"/> values
    /// and disjoint UID sets force the rank-comparer's ordinal
    /// tiebreaker -- both end up as canonicals (neither is a subset
    /// of the other), so the sort order determines the canonical
    /// emit sequence and the tiebreaker prevents flaky ordering.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCanonicalsAndBroadcastsBreaksRankTiesByOrdinalTfm()
    {
        var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> uidsA = ["T:Sample.A"];
        HashSet<string> uidsB = ["T:Sample.B"];
        var probes = new[]
        {
            new ProbedTfm("net10.0-windows", ["A.dll"], fallback, uidsA, Rank: 100),
            new ProbedTfm("net10.0-android", ["B.dll"], fallback, uidsB, Rank: 100),
        };

        var groups = NuGetAssemblySource.SelectCanonicalsAndBroadcasts(probes);

        await Assert.That(groups.Length).IsEqualTo(2);

        // Ordinal sort puts "net10.0-android" before "net10.0-windows" -- 'a' < 'w'.
        // The ranker emits in ordinal order when ranks tie.
        await Assert.That(groups[0].Tfm).IsEqualTo("net10.0-android");
        await Assert.That(groups[1].Tfm).IsEqualTo("net10.0-windows");
    }

    /// <summary>
    /// When a TFM directory's only DLLs are shadowed by the matching
    /// refs/ entries it contributes zero walkable assemblies, so the
    /// probe loop drops that slot. With another TFM still emitting,
    /// the result array is shorter than the scratch array and the
    /// trailing trim path (<c>count != slots.Length</c>) executes.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverAsyncTrimsProbeArrayWhenSomeTfmsYieldNoDlls()
    {
        using var root = new TempDirectory();
        using var api = new TempDirectory();

        // net10.0: a real package DLL survives filtering.
        var net10Lib = Path.Combine(api.Path, "lib", "net10.0");
        Directory.CreateDirectory(net10Lib);
        await File.WriteAllBytesAsync(Path.Combine(net10Lib, "Package.dll"), []);

        // net9.0: the only DLL is shadowed by a co-located ref, so the
        // post-filter package DLL list is empty and the probe slot is
        // skipped -- forcing the probe loop's trim branch.
        var net9Lib = Path.Combine(api.Path, "lib", "net9.0");
        var net9Refs = Path.Combine(api.Path, "refs", "net9.0");
        Directory.CreateDirectory(net9Lib);
        Directory.CreateDirectory(net9Refs);
        await File.WriteAllBytesAsync(Path.Combine(net9Lib, "Shadowed.dll"), []);
        await File.WriteAllBytesAsync(Path.Combine(net9Refs, "Shadowed.dll"), []);

        var source = new NuGetAssemblySource(root.Path, api.Path, logger: null, fetcher: new NoOpFetcher());
        List<AssemblyGroup> groups = [];
        await foreach (var group in source.DiscoverAsync())
        {
            groups.Add(group);
        }

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo("net10.0");
    }

    /// <summary>
    /// Whitespace-only <c>rootDirectory</c> trips the
    /// <see cref="ArgumentException.ThrowIfNullOrWhiteSpace"/> guard --
    /// distinct branch from the null check covered above.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsWhitespaceRootDirectory()
    {
        var apiPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "api");

        await Assert.That(Act).Throws<ArgumentException>();

        void Act() => _ = new NuGetAssemblySource(rootDirectory: "   ", apiPath: apiPath);
    }

    /// <summary>
    /// Whitespace-only <c>apiPath</c> trips the
    /// <see cref="ArgumentException.ThrowIfNullOrWhiteSpace"/> guard.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsWhitespaceApiPath()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "repo");

        await Assert.That(Act).Throws<ArgumentException>();

        void Act() => _ = new NuGetAssemblySource(rootDirectory: rootDirectory, apiPath: "   ");
    }

    /// <summary>
    /// Disposable scratch directory the test deletes on dispose.
    /// </summary>
    private sealed class TempDirectory : IDisposable
    {
        /// <summary>Initializes a new instance of the <see cref="TempDirectory"/> class.</summary>
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sdp-nuget-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the absolute path of the scratch directory.</summary>
        public string Path { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            Directory.Delete(Path, recursive: true);
        }
    }

    /// <summary>
    /// Fetcher fake used by discovery tests that prepare the lib/refs layout directly.
    /// </summary>
    private sealed class NoOpFetcher : INuGetFetcher
    {
        /// <inheritdoc/>
        public Task FetchPackagesAsync(string rootDirectory, string apiPath) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
