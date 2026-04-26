// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="NuGetConfigDiscovery"/> against fixture
/// folders that mirror what NuGet sees in the wild — a config
/// at the working folder, a config one or more levels up, and
/// the Windows-style <c>NuGet.Config</c> capitalisation we
/// also have to honour on case-sensitive filesystems.
/// </summary>
public class NuGetConfigDiscoveryTests
{
    /// <summary>
    /// Working folder = the fixture itself. The config sitting
    /// next to it is the first hit; <c>ResolveAsync</c> returns
    /// the value it carries.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolvesGlobalFolderFromConfigInWorkingDirectory()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-global");

        var resolved = await NuGetConfigDiscovery.ResolveAsync(fixture).ConfigureAwait(false);

        await Assert.That(resolved).IsEqualTo("/custom/packages");
    }

    /// <summary>
    /// Walk-up: working folder is several levels deep under the
    /// fixture and the config sits at the fixture root. The
    /// discovery walks up and lands on the parent's config.
    /// Empty subdirs aren't preserved by the file-glob copy step,
    /// so the test materialises them at runtime.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalksUpToFindParentConfig()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "walk-up");
        var deepWorkingFolder = Path.Combine(fixtureRoot, "sub", "nested");
        Directory.CreateDirectory(deepWorkingFolder);

        var resolved = await NuGetConfigDiscovery.ResolveAsync(deepWorkingFolder).ConfigureAwait(false);

        await Assert.That(resolved).IsEqualTo("/found/by/walk-up");
    }

    /// <summary>
    /// Windows-style capitalisation (<c>NuGet.Config</c>) is
    /// honoured even on case-sensitive filesystems — both
    /// filename casings are probed at every level.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RecognisesWindowsStyleNuGetConfigCasing()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "windows-style");

        var resolved = await NuGetConfigDiscovery.ResolveAsync(fixture).ConfigureAwait(false);

        await Assert.That(resolved).IsEqualTo(@"C:\packages\custom");
    }

    /// <summary>
    /// Working folder with no configs anywhere up to root falls
    /// back through to the platform default — but the env-var
    /// override (set here) wins over any default.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FallsBackToPlatformDefaultWhenNoConfigFound()
    {
        var emptyFolder = Path.Combine(Path.GetTempPath(), $"sdp-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyFolder);
        var originalEnv = Environment.GetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar);

        try
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, "/from-env-var");

            var resolved = await NuGetConfigDiscovery.ResolveAsync(emptyFolder).ConfigureAwait(false);

            await Assert.That(resolved).IsEqualTo("/from-env-var");
        }
        finally
        {
            Environment.SetEnvironmentVariable(NuGetGlobalCache.GlobalPackagesFolderEnvVar, originalEnv);
            if (Directory.Exists(emptyFolder))
            {
                Directory.Delete(emptyFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// EnumerateConfigPaths visits the working folder first, then
    /// each ancestor — verifies precedence shape directly so
    /// regressions in the walk get a focused failure.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumerateConfigPathsVisitsWorkingFolderFirst()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-global");

        var first = NuGetConfigDiscovery.EnumerateConfigPaths(fixture).FirstOrDefault();

        await Assert.That(first).IsEqualTo(Path.Combine(fixture, "nuget.config"));
    }
}
