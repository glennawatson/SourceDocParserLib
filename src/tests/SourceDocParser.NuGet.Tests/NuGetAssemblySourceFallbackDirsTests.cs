// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="NuGetAssemblySource.BuildFallbackDirList"/> -- the
/// helper that decides which lib/ buckets the assembly resolver gets
/// to fall back through when a primary DLL references a transitively-
/// pulled package that targets a lower TFM. The bug this fixes
/// surfaced as <c>Unable to resolve assembly reference 'System.Reactive ...'</c>
/// during downstream metadata extraction; the fix is to
/// include every runtime-compatible TFM directory in the fallback
/// scan, not just the consumer's exact lib/ bucket.
/// </summary>
public class NuGetAssemblySourceFallbackDirsTests
{
    /// <summary>Fixture value for Nuget.</summary>
    private const string Nuget = "nuget";

    /// <summary>Fixture value for Net80.</summary>
    private const string Net80 = "net8.0";

    /// <summary>Fixture value for Netstandard20.</summary>
    private const string Netstandard20 = "netstandard2.0";

    /// <summary>Fixture value for Net60.</summary>
    private const string Net60 = "net6.0";

    /// <summary>Fixture value for NugetRefs.</summary>
    private const string NugetRefs = "nuget/refs";

    /// <summary>Expected fixture value used by BuildFallbackDirListPlacesSdkRefPackDirsLast.</summary>
    private const int BuildFallbackDirListPlacesSdkRefPackDirsLastThat = 2;

    /// <summary>
    /// Net 8.0 consumer pulls in a package shipped under
    /// <c>netstandard2.0</c> -- the fallback list must include the
    /// netstandard dir so the resolver can find that DLL.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListIncludesLowerCompatibleTfms()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var libTfms = new List<string> { Net80, Netstandard20, Net60 };
        var libTfmDir = Path.Combine(libDir, Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: []);

        await Assert.That(dirs).Contains(libTfmDir);
        await Assert.That(dirs).Contains(Path.Combine(libDir, Net60));
        await Assert.That(dirs).Contains(Path.Combine(libDir, Netstandard20));
    }

    /// <summary>
    /// The target TFM's own lib dir comes first (after the optional
    /// refs prefix) so DLLs shipped under the consumer's own bucket
    /// win on duplicate filenames.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListPlacesTargetTfmBeforeLowerCompatibles()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var libTfms = new List<string> { Netstandard20, Net60, Net80 };
        var libTfmDir = Path.Combine(libDir, Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: []);

        var targetIndex = dirs.IndexOf(libTfmDir);
        var net6Index = dirs.IndexOf(Path.Combine(libDir, Net60));
        var netstandardIndex = dirs.IndexOf(Path.Combine(libDir, Netstandard20));

        await Assert.That(targetIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(targetIndex).IsLessThan(net6Index);
        await Assert.That(net6Index).IsLessThan(netstandardIndex);
    }

    /// <summary>
    /// The refs/ dir wins first slot when a best matching ref TFM is
    /// supplied -- ref-pack DLLs (clean public surface) take priority
    /// over the lib/ implementations.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListPlacesRefsDirFirstWhenBestRefSupplied()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var refsDir = Path.Combine(Nuget, "refs");
        var libTfms = new List<string> { Net80, Netstandard20 };
        var libTfmDir = Path.Combine(libDir, Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: refsDir,
            bestRefTfm: Net80,
            sdkRefPackDirs: []);

        await Assert.That(dirs[0]).IsEqualTo(Path.Combine(refsDir, Net80));
        await Assert.That(dirs[1]).IsEqualTo(libTfmDir);
    }

    /// <summary>
    /// The target TFM appears exactly once even when it's also the
    /// best ref TFM -- duplicates would only cost CPU on the index
    /// scan, never correctness.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListDoesNotDuplicateTargetTfm()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var libTfms = new List<string> { Net80, Net60 };
        var libTfmDir = Path.Combine(libDir, Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: []);

        var targetMatches = 0;
        for (var i = 0; i < dirs.Count; i++)
        {
            if (string.Equals(dirs[i], libTfmDir, StringComparison.Ordinal))
            {
                targetMatches++;
            }
        }

        await Assert.That(targetMatches).IsEqualTo(1);
    }

    /// <summary>
    /// A net48 lib dir is incompatible with a net8.0 consumer and
    /// must not appear in the fallback list -- adding it could mask
    /// real version mismatches with stale BCL contracts.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListSkipsIncompatibleLibDirs()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var libTfms = new List<string> { Net80, "net48" };
        var libTfmDir = Path.Combine(libDir, Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: []);

        await Assert.That(dirs).DoesNotContain(Path.Combine(libDir, "net48"));
    }

    /// <summary>
    /// When the target TFM is the only compatible bucket (no refs,
    /// no lower compatibles) the result is a single-element list --
    /// nothing extra to scan.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListReturnsSingleEntryWhenNoCompatibleSiblings()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var libTfms = new List<string> { Net80 };
        var libTfmDir = Path.Combine(libDir, Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: []);

        await Assert.That(dirs.Count).IsEqualTo(1);
        await Assert.That(dirs[0]).IsEqualTo(libTfmDir);
    }

    /// <summary>
    /// SDK ref-pack dirs are appended at the END of the fallback list
    /// so DLLs shipped with the consumer's lib/ always win duplicate
    /// names against the platform refs.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListPlacesSdkRefPackDirsLast()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var libTfms = new List<string> { Net80, Net60 };
        var libTfmDir = Path.Combine(libDir, Net80);
        var sdkPack1 = Path.Combine("dotnet", "packs", "Microsoft.WindowsDesktop.App.Ref", "8.0.10", "ref", Net80);
        var sdkPack2 = Path.Combine("dotnet", "packs", "Microsoft.NETCore.App.Ref", "8.0.10", "ref", Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: [sdkPack1, sdkPack2]);

        await Assert.That(dirs[0]).IsEqualTo(libTfmDir);
        await Assert.That(dirs[^BuildFallbackDirListPlacesSdkRefPackDirsLastThat]).IsEqualTo(sdkPack1);
        await Assert.That(dirs[^1]).IsEqualTo(sdkPack2);
    }

    /// <summary>
    /// Empty <c>sdkRefPackDirs</c> -- the standard input on machines
    /// without any SDK installed -- doesn't change the rest of the
    /// fallback ordering.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListWorksWithEmptySdkRefPackDirs()
    {
        var libDir = Path.Combine(Nuget, "lib");
        var libTfms = new List<string> { Net80, Netstandard20 };
        var libTfmDir = Path.Combine(libDir, Net80);

        var dirs = NuGetAssemblySource.BuildFallbackDirList(
            libDir,
            libTfms,
            targetTfm: Net80,
            libTfmDir: libTfmDir,
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: []);

        // Without SDK packs the list is exactly: target + compatible
        // libs (which here means just netstandard2.0) -- no trailing
        // pack dirs.
        await Assert.That(dirs.Count).IsEqualTo(BuildFallbackDirListPlacesSdkRefPackDirsLastThat);
        await Assert.That(dirs[0]).IsEqualTo(libTfmDir);
        await Assert.That(dirs[1]).IsEqualTo(Path.Combine(libDir, Netstandard20));
    }

    /// <summary>Null <c>sdkRefPackDirs</c> is rejected with the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallbackDirListRejectsNullSdkRefPackDirs() => await Assert.That(static () => NuGetAssemblySource.BuildFallbackDirList(
            "nuget/lib",
            [Net80],
            targetTfm: Net80,
            libTfmDir: "nuget/lib/net8.0",
            refsDir: NugetRefs,
            bestRefTfm: null,
            sdkRefPackDirs: null!)).Throws<ArgumentNullException>();
}
