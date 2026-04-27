// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins the OS-conditional branches of the user / machine
/// nuget.config path resolution + the user-profile fallback that the
/// non-substitutable <c>OperatingSystem.IsWindows()</c> /
/// <c>Environment.GetFolderPath</c> calls hide inside
/// <see cref="NuGetGlobalCache"/>.
/// </summary>
public class NuGetConfigPathsResolverTests
{
    /// <summary>Windows path pulls from <c>%AppData%\NuGet\NuGet.Config</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetUserPathsReturnsWindowsAppDataPath()
    {
        var paths = NuGetConfigPathsResolver.GetUserPaths(isWindows: true, appData: "/Users/u/AppData/Roaming", userProfile: null);

        await Assert.That(paths.Length).IsEqualTo(1);
        await Assert.That(paths[0]).EndsWith(Path.Combine("NuGet", "NuGet.Config"));
        await Assert.That(paths[0]).StartsWith("/Users/u/AppData/Roaming");
    }

    /// <summary>Windows + missing AppData yields an empty array.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetUserPathsReturnsEmptyWhenWindowsAppDataMissing()
    {
        var paths = NuGetConfigPathsResolver.GetUserPaths(isWindows: true, appData: null, userProfile: "/home/u");

        await Assert.That(paths).IsEmpty();
    }

    /// <summary>Unix returns both the legacy and XDG-style paths.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetUserPathsReturnsBothUnixCandidates()
    {
        var paths = NuGetConfigPathsResolver.GetUserPaths(isWindows: false, appData: null, userProfile: "/home/u");

        await Assert.That(paths.Length).IsEqualTo(2);
        await Assert.That(paths[0]).EndsWith(Path.Combine(".nuget", "NuGet", "NuGet.Config"));
        await Assert.That(paths[1]).EndsWith(Path.Combine(".config", "NuGet", "NuGet.Config"));
    }

    /// <summary>Unix + missing user profile yields an empty array.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetUserPathsReturnsEmptyWhenUnixUserProfileMissing()
    {
        var paths = NuGetConfigPathsResolver.GetUserPaths(isWindows: false, appData: "/anything", userProfile: null);

        await Assert.That(paths).IsEmpty();
    }

    /// <summary>A blank machine root returns empty without touching the disk.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetMachinePathsReturnsEmptyForBlankRoot()
    {
        await Assert.That(NuGetConfigPathsResolver.GetMachinePaths(null)).IsEmpty();
        await Assert.That(NuGetConfigPathsResolver.GetMachinePaths("   ")).IsEmpty();
    }

    /// <summary>A non-existent root returns empty.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetMachinePathsReturnsEmptyWhenRootMissing()
    {
        var paths = NuGetConfigPathsResolver.GetMachinePaths("/no/such/root/should/not/exist");

        await Assert.That(paths).IsEmpty();
    }

    /// <summary>An existing root returns every <c>*.config</c> file under it, sorted ordinal.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetMachinePathsReturnsSortedConfigFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdp-machine-cfg-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            await File.WriteAllTextAsync(Path.Combine(root, "Z.config"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "A.config"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "sub", "M.config"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "skipped.txt"), string.Empty);

            var paths = NuGetConfigPathsResolver.GetMachinePaths(root);

            await Assert.That(paths.Length).IsEqualTo(3);
            for (var i = 1; i < paths.Length; i++)
            {
                await Assert.That(StringComparer.Ordinal.Compare(paths[i - 1], paths[i])).IsLessThan(0);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>The default global packages folder uses the user profile when present.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDefaultGlobalPackagesFolderUsesUserProfile()
    {
        var path = NuGetConfigPathsResolver.GetDefaultGlobalPackagesFolder("/home/u");

        await Assert.That(path).IsEqualTo(Path.Combine("/home/u", PathSeparatorHelpers.ToPlatformPath(".nuget/packages")));
    }

    /// <summary>The default global packages folder falls back to cwd when the profile is blank.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetDefaultGlobalPackagesFolderFallsBackToCwd()
    {
        var path = NuGetConfigPathsResolver.GetDefaultGlobalPackagesFolder(string.Empty);

        await Assert.That(path).StartsWith(Directory.GetCurrentDirectory());
        await Assert.That(path).EndsWith(PathSeparatorHelpers.ToPlatformPath(".nuget/packages"));
    }
}
