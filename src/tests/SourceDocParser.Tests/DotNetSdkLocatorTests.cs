// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="DotNetSdkLocator"/>'s discovery rules.
/// Every scenario is driven through a hand-built
/// <see cref="DotNetSdkLocatorInputs"/> snapshot so the tests stay
/// fully parallel-safe -- there's no process-wide env mutation, the
/// library is exercised as a pure function over its captured inputs.
/// </summary>
public class DotNetSdkLocatorTests
{
    /// <summary>The Snapshot factory captures something usable on the host.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SnapshotCapturesHostState()
    {
        var snap = DotNetSdkLocatorInputs.Snapshot();

        var anyOs = snap.IsWindows || snap.IsMacOs || snap.IsLinux;
        await Assert.That(anyOs).IsTrue();
    }

    /// <summary>
    /// EnumerateInstallRoots walks the priority chain: DOTNET_ROOT
    /// wins, then DOTNET_ROOT(x86), then PATH, then defaults, then
    /// user-profile.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumerateInstallRootsHonoursDotnetRootFirst()
    {
        using var scratch = new TempDirectory();
        var inputs = LinuxInputs() with { DotnetRoot = scratch.Path };

        var roots = DotNetSdkLocator.EnumerateInstallRoots(inputs);

        await Assert.That(roots.Count).IsGreaterThan(0);
        await Assert.That(roots[0]).IsEqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratch.Path)));
    }

    /// <summary>
    /// A non-existent DOTNET_ROOT is silently skipped -- the rest
    /// of the chain still drives discovery.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumerateInstallRootsIgnoresNonExistentDotnetRoot()
    {
        var bogus = "/nonexistent/sdp-test-" + Guid.NewGuid();
        var inputs = LinuxInputs() with { DotnetRoot = bogus };

        var roots = DotNetSdkLocator.EnumerateInstallRoots(inputs);

        for (var i = 0; i < roots.Count; i++)
        {
            await Assert.That(roots[i]).DoesNotContain("nonexistent");
        }
    }

    /// <summary>
    /// DOTNET_ROOT(x86) is honoured in second slot when the primary
    /// DOTNET_ROOT is unset.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumerateInstallRootsHonoursDotnetRootX86()
    {
        using var scratch = new TempDirectory();
        var inputs = LinuxInputs() with
        {
            DotnetRoot = null,
            DotnetRootX86 = scratch.Path,
        };

        var roots = DotNetSdkLocator.EnumerateInstallRoots(inputs);

        await Assert.That(roots.Count).IsGreaterThan(0);
        await Assert.That(roots[0]).IsEqualTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratch.Path)));
    }

    /// <summary>
    /// EnumeratePackRoots returns each install root's <c>packs/</c>
    /// subdir when present, in the same priority order.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumeratePackRootsIncludesPacksSubdirOfInstallRoot()
    {
        using var scratch = new TempDirectory();
        var packs = Path.Combine(scratch.Path, "packs");
        Directory.CreateDirectory(packs);
        var inputs = LinuxInputs() with { DotnetRoot = scratch.Path };

        var packRoots = DotNetSdkLocator.EnumeratePackRoots(inputs);
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packs));
        var matched = false;
        for (var i = 0; i < packRoots.Count; i++)
        {
            if (string.Equals(packRoots[i], expected, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
                break;
            }
        }

        await Assert.That(matched)
            .IsTrue()
            .Because($"expected pack roots to contain '{expected}', got [{string.Join(", ", packRoots)}]");
    }

    /// <summary>
    /// Fresh installs without any <c>packs/</c> subdir contribute
    /// nothing -- their install roots are silently filtered out.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumeratePackRootsSkipsRootsWithoutPacksFolder()
    {
        using var scratch = new TempDirectory();
        var inputs = LinuxInputs() with { DotnetRoot = scratch.Path };

        var packRoots = DotNetSdkLocator.EnumeratePackRoots(inputs);

        var unwanted = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(scratch.Path, "packs")));
        for (var i = 0; i < packRoots.Count; i++)
        {
            await Assert.That(packRoots[i]).IsNotEqualTo(unwanted);
        }
    }

    /// <summary>
    /// GetDefaultInstallRoots returns Windows ProgramFiles candidates
    /// when the snapshot says we're on Windows, regardless of the
    /// real host OS.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDefaultInstallRootsHonoursWindowsSnapshot()
    {
        var inputs = new DotNetSdkLocatorInputs(
            DotnetRoot: null,
            DotnetRootX86: null,
            Path: null,
            UserProfile: null,
            ProgramFiles: @"C:\Program Files",
            ProgramFilesX86: @"C:\Program Files (x86)",
            IsWindows: true,
            IsMacOs: false,
            IsLinux: false);

        var roots = DotNetSdkLocator.GetDefaultInstallRoots(inputs);

        await Assert.That(roots.Count).IsEqualTo(2);
        await Assert.That(roots[0]).EndsWith("dotnet");
        await Assert.That(roots[1]).EndsWith("dotnet");
    }

    /// <summary>GetDefaultInstallRoots returns the macOS path when the snapshot says macOS.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDefaultInstallRootsHonoursMacOsSnapshot()
    {
        var inputs = MacOsInputs();

        var roots = DotNetSdkLocator.GetDefaultInstallRoots(inputs);

        await Assert.That(roots.Count).IsEqualTo(1);
        await Assert.That(roots[0]).IsEqualTo("/usr/local/share/dotnet");
    }

    /// <summary>GetDefaultInstallRoots returns both Linux candidates.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDefaultInstallRootsHonoursLinuxSnapshot()
    {
        var roots = DotNetSdkLocator.GetDefaultInstallRoots(LinuxInputs());

        await Assert.That(roots.Count).IsEqualTo(2);
        await Assert.That(roots[0]).IsEqualTo("/usr/share/dotnet");
        await Assert.That(roots[1]).IsEqualTo("/usr/lib/dotnet");
    }

    /// <summary>GetDefaultInstallRoots returns no candidates when no platform flag is set.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDefaultInstallRootsReturnsEmptyForUnknownPlatform()
    {
        var inputs = new DotNetSdkLocatorInputs(
            DotnetRoot: null,
            DotnetRootX86: null,
            Path: null,
            UserProfile: null,
            ProgramFiles: null,
            ProgramFilesX86: null,
            IsWindows: false,
            IsMacOs: false,
            IsLinux: false);

        var roots = DotNetSdkLocator.GetDefaultInstallRoots(inputs);

        await Assert.That(roots.Count).IsEqualTo(0);
    }

    /// <summary>GetUserDotnetRoot returns null when the snapshot has no UserProfile.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetUserDotnetRootReturnsNullWhenUserProfileMissing()
    {
        var inputs = LinuxInputs() with { UserProfile = null };

        await Assert.That(DotNetSdkLocator.GetUserDotnetRoot(inputs)).IsNull();
    }

    /// <summary>GetUserDotnetRoot composes <c>{UserProfile}/.dotnet</c> when present.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetUserDotnetRootComposesProfileDotDotnet()
    {
        var inputs = LinuxInputs() with { UserProfile = "/home/test-user" };

        await Assert.That(DotNetSdkLocator.GetUserDotnetRoot(inputs)).IsEqualTo(Path.Combine("/home/test-user", ".dotnet"));
    }

    /// <summary>GetDotnetDirFromPath returns null when PATH is unset on the snapshot.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDotnetDirFromPathReturnsNullWithoutPath()
    {
        var inputs = LinuxInputs() with { Path = null };

        await Assert.That(DotNetSdkLocator.GetDotnetDirFromPath(inputs)).IsNull();
    }

    /// <summary>GetDotnetDirFromPath finds the dotnet executable on the synthetic PATH.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDotnetDirFromPathReturnsParentOfDotnetExecutable()
    {
        using var scratch = new TempDirectory();
        var dotnetPath = Path.Combine(scratch.Path, "dotnet");
        await File.WriteAllTextAsync(dotnetPath, string.Empty);

        var inputs = LinuxInputs() with { Path = scratch.Path };

        await Assert.That(DotNetSdkLocator.GetDotnetDirFromPath(inputs)).IsEqualTo(scratch.Path);
    }

    /// <summary>
    /// Concurrent <see cref="DotNetSdkLocator.EnumerateInstallRoots(DotNetSdkLocatorInputs)"/>
    /// calls against distinct snapshots produce distinct results --
    /// proves the function holds no shared mutable state.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumerateInstallRootsIsThreadSafeAcrossDistinctSnapshots()
    {
        using var scratchA = new TempDirectory();
        using var scratchB = new TempDirectory();
        var inputsA = LinuxInputs() with { DotnetRoot = scratchA.Path };
        var inputsB = LinuxInputs() with { DotnetRoot = scratchB.Path };

        var tasks = new Task<IReadOnlyList<string>>[100];
        for (var i = 0; i < tasks.Length; i++)
        {
            var pick = i % 2 is 0 ? inputsA : inputsB;
            tasks[i] = Task.Run(() => DotNetSdkLocator.EnumerateInstallRoots(pick));
        }

        var results = await Task.WhenAll(tasks);
        var expectedA = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchA.Path));
        var expectedB = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchB.Path));
        for (var i = 0; i < results.Length; i++)
        {
            var expected = i % 2 is 0 ? expectedA : expectedB;
            await Assert.That(results[i].Count).IsGreaterThan(0);
            await Assert.That(results[i][0]).IsEqualTo(expected);
        }
    }

    /// <summary>
    /// Builds a minimal Linux snapshot with empty env vars -- used as
    /// a base in <c>with</c> expressions when a test only cares about
    /// one or two fields.
    /// </summary>
    /// <returns>The base snapshot.</returns>
    private static DotNetSdkLocatorInputs LinuxInputs() => new(
        DotnetRoot: null,
        DotnetRootX86: null,
        Path: null,
        UserProfile: null,
        ProgramFiles: null,
        ProgramFilesX86: null,
        IsWindows: false,
        IsMacOs: false,
        IsLinux: true);

    /// <summary>Builds a minimal macOS snapshot.</summary>
    /// <returns>The base snapshot.</returns>
    private static DotNetSdkLocatorInputs MacOsInputs() => new(
        DotnetRoot: null,
        DotnetRootX86: null,
        Path: null,
        UserProfile: null,
        ProgramFiles: null,
        ProgramFilesX86: null,
        IsWindows: false,
        IsMacOs: true,
        IsLinux: false);
}
