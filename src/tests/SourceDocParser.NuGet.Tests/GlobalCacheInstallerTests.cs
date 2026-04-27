// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="GlobalCacheInstaller"/>: argument validation on the
/// constructor, the "uninitialized" guards on the public properties +
/// <see cref="GlobalCacheInstaller.InstallAsync"/>, and the integration
/// shape of <see cref="GlobalCacheInstaller.InitializeAsync"/> against
/// the existing nuget.config fixtures. The HTTP install path needs a
/// real feed and is exercised only by the integration tests.
/// </summary>
public class GlobalCacheInstallerTests
{
    /// <summary>Constructor rejects a null/blank working folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorRejectsBlankWorkingFolder()
    {
        await Assert.That(() => new GlobalCacheInstaller(string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => new GlobalCacheInstaller("   ")).Throws<ArgumentException>();
    }

    /// <summary>The <c>GlobalPackagesFolder</c> property throws until <c>InitializeAsync</c> has run.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GlobalPackagesFolderThrowsBeforeInitialize()
    {
        using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

        await Assert.That(() => installer.GlobalPackagesFolder).Throws<InvalidOperationException>();
    }

    /// <summary>The <c>EnabledSources</c> property throws until <c>InitializeAsync</c> has run.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnabledSourcesThrowsBeforeInitialize()
    {
        using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

        await Assert.That(() => installer.EnabledSources).Throws<InvalidOperationException>();
    }

    /// <summary><c>InstallAsync</c> throws when the installer hasn't been initialised.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallAsyncThrowsBeforeInitialize()
    {
        using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

        await Assert.That(() => installer.InstallAsync("Foo", "1.0.0"))
            .Throws<InvalidOperationException>();
    }

    /// <summary><c>InstallAsync</c> rejects blank package id / version arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallAsyncRejectsBlankArguments()
    {
        using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

        await Assert.That(() => installer.InstallAsync(string.Empty, "1.0.0")).Throws<ArgumentException>();
        await Assert.That(() => installer.InstallAsync("Foo", string.Empty)).Throws<ArgumentException>();
    }

    /// <summary>
    /// <c>InitializeAsync</c> integrates the nuget.config discovery
    /// chain with <see cref="GlobalCacheInstaller.GlobalPackagesFolder"/>
    /// + <see cref="GlobalCacheInstaller.EnabledSources"/>. The
    /// <c>with-sources</c> fixture declares two sources and a custom
    /// global packages folder.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InitializeAsyncResolvesFromFixture()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-sources");
        using var installer = new GlobalCacheInstaller(fixture);

        await installer.InitializeAsync().ConfigureAwait(false);

        // The fixture is one of the existing NuGetConfigDiscoveryTests
        // fixtures and declares two sources without a <clear/>.
        await Assert.That(installer.EnabledSources.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(installer.GlobalPackagesFolder).IsNotEmpty();
    }
}
