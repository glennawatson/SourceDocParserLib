// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins the global-packages-folder resolution helpers in
/// <see cref="NuGetGlobalCache"/> -- replaces the surface we'd
/// otherwise pull in from NuGet.Configuration / NuGet.Packaging
/// (and through them Newtonsoft.Json + dual JSON stacks).
/// </summary>
public class NuGetGlobalCacheTests
{
    /// <summary>
    /// NUGET_PACKAGES env var has top precedence -- overrides both
    /// any nuget.config setting and the platform default.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveGlobalPackagesFolderHonoursEnvVar()
    {
        var original = Environment.GetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, "/custom/cache");

            var resolved = NuGetGlobalCache.ResolveGlobalPackagesFolder(configOverride: "/should-be-ignored");

            await Assert.That(resolved).IsEqualTo("/custom/cache");
        }
        finally
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, original);
        }
    }

    /// <summary>
    /// With no env var, an explicit nuget.config override wins
    /// over the platform default.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveGlobalPackagesFolderHonoursConfigOverride()
    {
        var original = Environment.GetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, null);

            var resolved = NuGetGlobalCache.ResolveGlobalPackagesFolder(configOverride: "/from/config");

            await Assert.That(resolved).IsEqualTo("/from/config");
        }
        finally
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, original);
        }
    }

    /// <summary>
    /// With no env var and no config override, falls back to the
    /// platform default -- <c>$HOME/.nuget/packages</c> on Unix,
    /// <c>%USERPROFILE%\.nuget\packages</c> on Windows.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveGlobalPackagesFolderFallsBackToPlatformDefault()
    {
        var original = Environment.GetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, null);

            var resolved = NuGetGlobalCache.ResolveGlobalPackagesFolder(configOverride: null);

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var expected = Path.Combine(userProfile, ".nuget", "packages");
            await Assert.That(resolved).IsEqualTo(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, original);
        }
    }

    /// <summary>
    /// Per-package install path lowercases both id and version --
    /// matches NuGet's canonical layout so the "already extracted"
    /// probe finds the SDK-written marker.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetPackageInstallPathLowercasesIdAndVersion()
    {
        var path = NuGetGlobalCache.GetPackageInstallPath("/cache", "ReactiveUI", "23.2.1");

        var expected = Path.Combine("/cache", "reactiveui", "23.2.1");
        await Assert.That(path).IsEqualTo(expected);
    }

    /// <summary>
    /// Per-TFM lib path is just <c>{packageDir}/lib/{tfm}/</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetLibTfmPathComposesPackageRootWithTfmFolder()
    {
        var path = NuGetGlobalCache.GetLibTfmPath("/cache/splat/19.3.1", "net8.0");

        var expected = Path.Combine("/cache/splat/19.3.1", "lib", "net8.0");
        await Assert.That(path).IsEqualTo(expected);
    }

    /// <summary>
    /// IsPackageInstalled returns true when the SDK's
    /// <c>.nupkg.metadata</c> marker is present, false when
    /// the directory exists but the marker doesn't.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPackageInstalledChecksForNupkgMetadataMarker()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdp-globalcache-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            await Assert.That(NuGetGlobalCache.IsPackageInstalled(dir)).IsFalse();

            var marker = Path.Combine(dir, ".nupkg.metadata");
            await File.WriteAllTextAsync(marker, "{}").ConfigureAwait(false);
            await Assert.That(NuGetGlobalCache.IsPackageInstalled(dir)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// User-scoped nuget.config locations differ per platform --
    /// the helper returns the right candidate(s) for the current
    /// OS in precedence order.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetUserNuGetConfigPathsReturnsPlatformAppropriateLocations()
    {
        var paths = NuGetGlobalCache.GetUserNuGetConfigPaths();

        await Assert.That(paths.Length).IsGreaterThan(0);
        for (var i = 0; i < paths.Length; i++)
        {
            await Assert.That(paths[i]).EndsWith("NuGet.Config");
        }
    }

    /// <summary>
    /// ProbeFallbackFolders checks each folder in order and returns
    /// the first one that contains a successful install marker.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProbeFallbackFoldersReturnsFirstMatchingPath()
    {
        var root1 = Path.Combine(Path.GetTempPath(), $"sdp-fallback1-{Guid.NewGuid():N}");
        var root2 = Path.Combine(Path.GetTempPath(), $"sdp-fallback2-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root1);
            Directory.CreateDirectory(root2);

            var fallbackFolders = new[] { root1, root2 };
            const string packageId = "Splat";
            const string packageVersion = "15.1.1";

            // Case 1: None installed
            var result = NuGetGlobalCache.ProbeFallbackFolders(fallbackFolders, packageId, packageVersion);
            await Assert.That(result).IsNull();

            // Case 2: Installed in the second one
            var install2 = NuGetGlobalCache.GetPackageInstallPath(root2, packageId, packageVersion);
            Directory.CreateDirectory(install2);
            await File.WriteAllTextAsync(Path.Combine(install2, ".nupkg.metadata"), "{}").ConfigureAwait(false);

            result = NuGetGlobalCache.ProbeFallbackFolders(fallbackFolders, packageId, packageVersion);
            await Assert.That(result).IsEqualTo(install2);

            // Case 3: Installed in the first one (should win)
            var install1 = NuGetGlobalCache.GetPackageInstallPath(root1, packageId, packageVersion);
            Directory.CreateDirectory(install1);
            await File.WriteAllTextAsync(Path.Combine(install1, ".nupkg.metadata"), "{}").ConfigureAwait(false);

            result = NuGetGlobalCache.ProbeFallbackFolders(fallbackFolders, packageId, packageVersion);
            await Assert.That(result).IsEqualTo(install1);
        }
        finally
        {
            if (Directory.Exists(root1))
            {
                Directory.Delete(root1, recursive: true);
            }

            if (Directory.Exists(root2))
            {
                Directory.Delete(root2, recursive: true);
            }
        }
    }
}
