// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="SyntheticPackageBackfill"/> -- the adapter that
/// scans extracted DLLs for synthetic framework refs and projects
/// them onto NuGet package IDs via
/// <see cref="KnownFrameworkPackageMap"/>. Drives the
/// "auto-fetch Microsoft.WindowsAppSDK when WinUI surfaces appear"
/// path inside the transitive walk.
/// Each scenario builds a tiny synthetic DLL whose
/// <c>AssemblyReference</c> table carries a known projection name
/// (e.g. <c>Microsoft.WinUI</c>). The DLL is dropped into a fake
/// <c>libDir/&lt;tfm&gt;/</c> tree so the scan walks the same shape
/// the fetcher emits.
/// </summary>
public class SyntheticPackageBackfillTests
{
    /// <summary>Fixture value for Net80.</summary>
    private const string Net80 = "net8.0";

    /// <summary>Fixture value for Consumer.</summary>
    private const string Consumer = "Consumer";

    /// <summary>Fixture value for MicrosoftWinUI.</summary>
    private const string MicrosoftWinUI = "Microsoft.WinUI";

    /// <summary>Fixture value for MicrosoftWindowsAppSDK.</summary>
    private const string MicrosoftWindowsAppSDK = "Microsoft.WindowsAppSDK";

    /// <summary>Expected fixture value used by DiscoverEmitsSeparatePackagesForDistinctSyntheticFamilies.</summary>
    private const int DiscoverEmitsSeparatePackagesForDistinctSyntheticFamiliesExpectedValue = 2;

    /// <summary>Happy path: a DLL with a synthetic <c>Microsoft.WinUI</c> ref surfaces <c>Microsoft.WindowsAppSDK</c> as a backfill target.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverEmitsMappedNuGetIdForSyntheticReference()
    {
        using var temp = new ScratchDirectory();
        var libDir = MakeLibTfm(temp.Path, Net80);
        EmitConsumerWithReference(libDir, Consumer, MicrosoftWinUI);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(temp.Path, seenIds);

        await Assert.That(backfill.Count).IsEqualTo(1);
        await Assert.That(backfill[0]).IsEqualTo(MicrosoftWindowsAppSDK);
    }

    /// <summary>
    /// Multiple consumers in different TFM dirs that each reference
    /// distinct WinUI projections collapse into a single
    /// <c>Microsoft.WindowsAppSDK</c> ID.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverDeduplicatesAcrossTfmDirsAndAssemblies()
    {
        using var temp = new ScratchDirectory();
        var net8 = MakeLibTfm(temp.Path, Net80);
        var ns = MakeLibTfm(temp.Path, "netstandard2.0");
        EmitConsumerWithReference(net8, "ConsumerA", MicrosoftWinUI);
        EmitConsumerWithReference(net8, "ConsumerB", "WinRT.Runtime");
        EmitConsumerWithReference(ns, "ConsumerC", "Microsoft.InteractiveExperiences.Projection");

        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(
            temp.Path,
            [with(StringComparer.OrdinalIgnoreCase)]);

        await Assert.That(backfill.Count).IsEqualTo(1);
        await Assert.That(backfill[0]).IsEqualTo(MicrosoftWindowsAppSDK);
    }

    /// <summary>
    /// Distinct synthetic families surface separately. <c>Microsoft.WinUI</c>
    /// produces <c>Microsoft.WindowsAppSDK</c> while
    /// <c>Microsoft.Web.WebView2.Wpf</c> produces
    /// <c>Microsoft.Web.WebView2</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverEmitsSeparatePackagesForDistinctSyntheticFamilies()
    {
        using var temp = new ScratchDirectory();
        var libDir = MakeLibTfm(temp.Path, "net8.0-windows10.0.19041.0");
        EmitConsumerWithReference(libDir, "WinUiConsumer", MicrosoftWinUI);
        EmitConsumerWithReference(libDir, "WebViewConsumer", "Microsoft.Web.WebView2.Wpf");

        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(
            temp.Path,
            [with(StringComparer.OrdinalIgnoreCase)]);

        await Assert.That(backfill.Count).IsEqualTo(DiscoverEmitsSeparatePackagesForDistinctSyntheticFamiliesExpectedValue);
        await Assert.That(backfill).Contains(MicrosoftWindowsAppSDK);
        await Assert.That(backfill).Contains("Microsoft.Web.WebView2");
    }

    /// <summary>Already-fetched IDs (carried in the seenIds set) are elided so the BFS doesn't re-queue them.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverSkipsPackageIdsAlreadyTrackedAsSeen()
    {
        using var temp = new ScratchDirectory();
        var libDir = MakeLibTfm(temp.Path, Net80);
        EmitConsumerWithReference(libDir, Consumer, MicrosoftWinUI);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MicrosoftWindowsAppSDK };

        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(temp.Path, seenIds);

        await Assert.That(backfill.Count).IsEqualTo(0);
    }

    /// <summary>Refs that don't appear in <see cref="KnownFrameworkPackageMap"/> (e.g. real NuGet packages we already know how to fetch) are passed over silently.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverIgnoresRefsWithoutAMappingEntry()
    {
        using var temp = new ScratchDirectory();
        var libDir = MakeLibTfm(temp.Path, Net80);

        // System.Reactive is on NuGet under its own name; it has no
        // synthetic-projection mapping and shouldn't be backfilled.
        EmitConsumerWithReference(libDir, Consumer, "System.Reactive");

        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(
            temp.Path,
            [with(StringComparer.OrdinalIgnoreCase)]);

        await Assert.That(backfill.Count).IsEqualTo(0);
    }

    /// <summary>Non-managed garbage on disk (an empty file with a <c>.dll</c> extension) is silently skipped via <see cref="BadImageFormatException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverIgnoresMalformedDllFilesWithoutThrowing()
    {
        using var temp = new ScratchDirectory();
        var libDir = MakeLibTfm(temp.Path, Net80);
        await File.WriteAllTextAsync(Path.Combine(libDir, "garbage.dll"), "not a managed assembly");
        EmitConsumerWithReference(libDir, Consumer, MicrosoftWinUI);

        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(
            temp.Path,
            [with(StringComparer.OrdinalIgnoreCase)]);

        await Assert.That(backfill).Contains(MicrosoftWindowsAppSDK);
    }

    /// <summary>Missing <c>libDir</c> short-circuits to an empty result without throwing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverReturnsEmptyForMissingLibDir()
    {
        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(
            $"/nonexistent/sdp-test-{Guid.NewGuid()}",
            [with(StringComparer.OrdinalIgnoreCase)]);

        await Assert.That(backfill.Count).IsEqualTo(0);
    }

    /// <summary>Empty <c>libDir</c> (no DLLs) yields an empty backfill list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverReturnsEmptyForEmptyLibDir()
    {
        using var temp = new ScratchDirectory();

        var backfill = SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(
            temp.Path,
            [with(StringComparer.OrdinalIgnoreCase)]);

        await Assert.That(backfill.Count).IsEqualTo(0);
    }

    /// <summary>Null-guards on the public API.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverRejectsBlankLibDirAndNullSeenSet()
    {
        await Assert.That(static () => SyntheticPackageBackfill.DiscoverFromExtractedAssemblies(string.Empty, [])).Throws<ArgumentException>();
        await Assert.That(static () => SyntheticPackageBackfill.DiscoverFromExtractedAssemblies("/tmp", null!)).Throws<ArgumentNullException>();
    }

    /// <summary>Builds <paramref name="parent"/>/<paramref name="tfm"/> as the per-TFM lib subdirectory the fetcher would extract into.</summary>
    /// <param name="parent">Lib root.</param>
    /// <param name="tfm">TFM directory name.</param>
    /// <returns>The created path.</returns>
    private static string MakeLibTfm(string parent, string tfm)
    {
        var dir = Path.Combine(parent, tfm);
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Emits <paramref name="consumerName"/>.dll into
    /// <paramref name="outputDir"/> with an <c>AssemblyReference</c>
    /// to a synthetic dependency named <paramref name="referenceName"/>.
    /// </summary>
    /// <param name="outputDir">Where the consumer DLL lands.</param>
    /// <param name="consumerName">Simple name of the consumer assembly.</param>
    /// <param name="referenceName">Simple name of the synthetic reference to embed.</param>
    private static void EmitConsumerWithReference(string outputDir, string consumerName, string referenceName)
    {
        // First emit a tiny dependency carrying a Marker type so the
        // consumer can pin the reference and Roslyn doesn't cull it.
        var depPath = Path.Combine(outputDir, $"{referenceName}.dll");
        var depCompilation = CSharpCompilation.Create(
            referenceName,
            [CSharpSyntaxTree.ParseText($"namespace {SafeNamespace(referenceName)};\npublic class Marker {{ }}")],
            BclReferences(),
            new(OutputKind.DynamicallyLinkedLibrary));
        EmitOrThrow(depCompilation, depPath);

        var consumerPath = Path.Combine(outputDir, $"{consumerName}.dll");
        var refs = new List<MetadataReference>(BclReferences()) { MetadataReference.CreateFromFile(depPath), };
        var consumerCompilation = CSharpCompilation.Create(
            consumerName,
            [CSharpSyntaxTree.ParseText($"namespace Test.Consumer;\npublic class P {{ public {SafeNamespace(referenceName)}.Marker M; }}")],
            refs,
            new(OutputKind.DynamicallyLinkedLibrary));
        EmitOrThrow(consumerCompilation, consumerPath);

        // We deliberately keep the synthetic dep DLL on disk too --
        // production extraction would do the same -- but the BFS is
        // looking at the consumer's AssemblyReference table, not the
        // dep's metadata, so its presence doesn't affect the test.
    }

    /// <summary>Materialises <paramref name="compilation"/> at <paramref name="path"/> or throws when Roslyn reports a compile failure.</summary>
    /// <param name="compilation">Compilation to emit.</param>
    /// <param name="path">Destination DLL path.</param>
    /// <exception cref="InvalidOperationException">The fixture compilation fails.</exception>
    private static void EmitOrThrow(CSharpCompilation compilation, string path)
    {
        var emit = compilation.Emit(path);
        if (emit.Success)
        {
            return;
        }

        throw new InvalidOperationException($"Compile failed: {string.Join('\n', emit.Diagnostics)}");
    }

    /// <summary>Replaces dots with underscores so the assembly name doubles as a valid namespace identifier.</summary>
    /// <param name="name">Source name.</param>
    /// <returns>Identifier-safe namespace.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string SafeNamespace(string name) => name.Replace('.', '_');

    /// <summary>BCL references picked up from the test runner's loaded assemblies.</summary>
    /// <returns>The reference set every synthetic compilation needs.</returns>
    private static IEnumerable<MetadataReference> BclReferences()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic || assembly.Location is not [_, ..])
            {
                continue;
            }

            yield return MetadataReference.CreateFromFile(assembly.Location);
        }
    }

    /// <summary>Self-cleaning scratch directory for per-test fixtures.</summary>
    private sealed class ScratchDirectory : IDisposable
    {
        /// <summary>Initializes a new instance of the <see cref="ScratchDirectory"/> class.</summary>
        public ScratchDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sdp-backfill-{Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)}");
            _ = Directory.CreateDirectory(Path);
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
}
