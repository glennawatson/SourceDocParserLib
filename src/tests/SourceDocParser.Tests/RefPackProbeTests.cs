// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="RefPackProbe"/> against synthetic on-disk pack
/// layouts so we can assert the exact filtering rules (.Ref suffix,
/// highest-version pick, ref/&lt;tfm&gt; subdir match) without a
/// real .NET SDK install.
/// </summary>
public class RefPackProbeTests
{
    /// <summary>A single pack with one matching TFM is discovered.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeReturnsRefDirForMatchingTfm()
    {
        using var scratch = new TempDirectory();
        var refDir = MakeRefDir(scratch.Path, "Microsoft.WindowsDesktop.App.Ref", "8.0.10", "net8.0");

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path], ["net8.0"]);

        await Assert.That(dirs.Count).IsEqualTo(1);
        await Assert.That(dirs[0]).IsEqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(refDir)));
    }

    /// <summary>Packs whose name does NOT end in <c>.Ref</c> are skipped (e.g. runtime packs).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeSkipsNonRefPacks()
    {
        using var scratch = new TempDirectory();
        MakeRefDir(scratch.Path, "Microsoft.WindowsDesktop.App", "8.0.10", "net8.0");

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path], ["net8.0"]);

        await Assert.That(dirs.Count).IsEqualTo(0);
    }

    /// <summary>The highest-numbered version dir wins -- older installs are ignored.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbePicksHighestStableVersion()
    {
        using var scratch = new TempDirectory();
        MakeRefDir(scratch.Path, "Microsoft.WindowsDesktop.App.Ref", "8.0.5", "net8.0");
        var winner = MakeRefDir(scratch.Path, "Microsoft.WindowsDesktop.App.Ref", "8.0.10", "net8.0");
        MakeRefDir(scratch.Path, "Microsoft.WindowsDesktop.App.Ref", "7.0.20", "net7.0");

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path], ["net8.0"]);

        await Assert.That(dirs.Count).IsEqualTo(1);
        await Assert.That(dirs[0]).IsEqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(winner)));
    }

    /// <summary>Compatible-TFM list is honoured -- net6.0 in pack matches when caller asks for both.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeReturnsRefDirsForCompatibleTfmList()
    {
        using var scratch = new TempDirectory();
        var net8 = MakeRefDir(scratch.Path, "Microsoft.NETCore.App.Ref", "8.0.10", "net8.0");
        var net6 = MakeRefDir(scratch.Path, "Microsoft.NETCore.App.Ref", "8.0.10", "net6.0");

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path], ["net8.0", "net6.0", "netstandard2.0"]);

        await Assert.That(dirs.Count).IsEqualTo(2);
        await Assert.That(dirs).Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(net8)));
        await Assert.That(dirs).Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(net6)));
    }

    /// <summary>Target TFM comes first in the result (lib-prio order honoured).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbePreservesCompatibleTfmOrder()
    {
        using var scratch = new TempDirectory();
        MakeRefDir(scratch.Path, "Microsoft.NETCore.App.Ref", "8.0.10", "net8.0");
        MakeRefDir(scratch.Path, "Microsoft.NETCore.App.Ref", "8.0.10", "net6.0");

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path], ["net8.0", "net6.0"]);

        await Assert.That(dirs.Count).IsEqualTo(2);
        await Assert.That(dirs[0]).EndsWith(Path.Combine("ref", "net8.0"));
        await Assert.That(dirs[1]).EndsWith(Path.Combine("ref", "net6.0"));
    }

    /// <summary>Missing <c>ref/</c> directory under the version dir is silently ignored.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeSkipsPacksWithoutRefSubdir()
    {
        using var scratch = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(scratch.Path, "Microsoft.iOS.Ref", "18.0.0"));

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path], ["net8.0-ios"]);

        await Assert.That(dirs.Count).IsEqualTo(0);
    }

    /// <summary>Empty pack roots / empty TFM list short-circuit cleanly.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeReturnsEmptyForEmptyInputs()
    {
        await Assert.That(RefPackProbe.ProbeRefPackRefDirs([], ["net8.0"]).Count).IsEqualTo(0);
        await Assert.That(RefPackProbe.ProbeRefPackRefDirs(["/some/path"], []).Count).IsEqualTo(0);
    }

    /// <summary>Non-existent pack root is ignored without exceptions.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeIgnoresMissingPackRoot()
    {
        var dirs = RefPackProbe.ProbeRefPackRefDirs(
            ["/nonexistent/path/sdp-test-" + Guid.NewGuid()],
            ["net8.0"]);

        await Assert.That(dirs.Count).IsEqualTo(0);
    }

    /// <summary>Multiple pack roots are de-duplicated when they point at the same dir.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeDeduplicatesAcrossPackRoots()
    {
        using var scratch = new TempDirectory();
        MakeRefDir(scratch.Path, "Microsoft.NETCore.App.Ref", "8.0.10", "net8.0");

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path, scratch.Path], ["net8.0"]);

        await Assert.That(dirs.Count).IsEqualTo(1);
    }

    /// <summary>Multiple distinct ref packs each contribute one entry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeAccumulatesAcrossDistinctPacks()
    {
        using var scratch = new TempDirectory();
        MakeRefDir(scratch.Path, "Microsoft.NETCore.App.Ref", "8.0.10", "net8.0");
        MakeRefDir(scratch.Path, "Microsoft.WindowsDesktop.App.Ref", "8.0.10", "net8.0");
        MakeRefDir(scratch.Path, "Microsoft.AspNetCore.App.Ref", "8.0.10", "net8.0");

        var dirs = RefPackProbe.ProbeRefPackRefDirs([scratch.Path], ["net8.0"]);

        await Assert.That(dirs.Count).IsEqualTo(3);
    }

    /// <summary>Rejects null arguments via the standard guards.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeRejectsNullArguments()
    {
        await Assert.That(() => RefPackProbe.ProbeRefPackRefDirs(null!, ["net8.0"])).Throws<ArgumentNullException>();
        await Assert.That(() => RefPackProbe.ProbeRefPackRefDirs([], null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Builds <c>&lt;packsRoot&gt;/&lt;packName&gt;/&lt;version&gt;/ref/&lt;tfm&gt;/</c>
    /// on disk and returns the path to the leaf <c>tfm</c> dir.
    /// </summary>
    /// <param name="packsRoot">Root of the synthetic packs/ tree.</param>
    /// <param name="packName">Pack folder name (e.g. <c>Microsoft.WindowsDesktop.App.Ref</c>).</param>
    /// <param name="version">Version folder name (e.g. <c>8.0.10</c>).</param>
    /// <param name="tfm">TFM folder name (e.g. <c>net8.0</c>).</param>
    /// <returns>The created leaf directory's path.</returns>
    private static string MakeRefDir(string packsRoot, string packName, string version, string tfm)
    {
        var path = Path.Combine(packsRoot, packName, version, "ref", tfm);
        Directory.CreateDirectory(path);
        return path;
    }
}
