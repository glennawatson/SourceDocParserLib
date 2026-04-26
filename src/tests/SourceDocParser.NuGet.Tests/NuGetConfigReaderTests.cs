// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="NuGetConfigReader"/> against fixture
/// <c>nuget.config</c> files copied to the test bin output. The
/// fixtures live under <c>Fixtures/{with-global,without-global}/nuget.config</c>
/// and exercise both branches of the
/// <c>globalPackagesFolder</c> probe — present and absent.
/// </summary>
public class NuGetConfigReaderTests
{
    /// <summary>
    /// A nuget.config with a globalPackagesFolder add inside the config
    /// section returns the configured value.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadsGlobalPackagesFolderFromConfigFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-global", "nuget.config");

        var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(path).ConfigureAwait(false);

        await Assert.That(result.State).IsEqualTo(SettingState.Found);
        await Assert.That(result.Value).IsEqualTo("/custom/packages");
    }

    /// <summary>
    /// A nuget.config without that key returns <see langword="null"/>
    /// — caller falls back to the next config in the chain or the
    /// platform default.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReturnsNotMentionedWhenSettingAbsent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "without-global", "nuget.config");

        var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(path).ConfigureAwait(false);

        await Assert.That(result.State).IsEqualTo(SettingState.NotMentioned);
        await Assert.That(result.Value).IsNull();
    }

    /// <summary>
    /// Settings outside the <c>&lt;config&gt;</c> container are
    /// ignored — packageSources also uses <c>add key=… value=…</c>
    /// and we don't want to pick up a <c>nuget.org</c> URL by
    /// accident.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IgnoresAddElementsOutsideConfigContainer()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="globalPackagesFolder" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(stream).ConfigureAwait(false);

        await Assert.That(result.State).IsEqualTo(SettingState.NotMentioned);
    }

    /// <summary>
    /// Empty / whitespace-only value is treated as "not set" so
    /// the caller falls through to the next layer.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReturnsNotMentionedWhenValueIsBlank()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="   " />
              </config>
            </configuration>
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(stream).ConfigureAwait(false);

        await Assert.That(result.State).IsEqualTo(SettingState.NotMentioned);
    }

    /// <summary>
    /// First <c>&lt;add&gt;</c> wins per NuGet's
    /// <c>GetFirstItemWithAttribute</c> rule — duplicates inside
    /// the same file are ignored.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FirstAddWinsOverDuplicates()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="/first" />
                <add key="globalPackagesFolder" value="/second" />
              </config>
            </configuration>
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(stream).ConfigureAwait(false);

        await Assert.That(result.State).IsEqualTo(SettingState.Found);
        await Assert.That(result.Value).IsEqualTo("/first");
    }

    /// <summary>
    /// <c>&lt;clear/&gt;</c> wipes earlier in-file accumulator —
    /// the post-clear add becomes the resolved value.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClearResetsAndPostClearAddWins()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="/before-clear" />
                <clear />
                <add key="globalPackagesFolder" value="/after-clear" />
              </config>
            </configuration>
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(stream).ConfigureAwait(false);

        await Assert.That(result.State).IsEqualTo(SettingState.Found);
        await Assert.That(result.Value).IsEqualTo("/after-clear");
    }

    /// <summary>
    /// <c>&lt;clear/&gt;</c> with no following <c>&lt;add&gt;</c>
    /// produces the <c>Cleared</c> tri-state — the discovery walk
    /// stops on this and falls through to the platform default,
    /// erasing any parent's value.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClearWithoutPostAddProducesClearedState()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="/before-clear" />
                <clear />
              </config>
            </configuration>
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var result = await NuGetConfigReader.ReadGlobalPackagesFolderAsync(stream).ConfigureAwait(false);

        await Assert.That(result.State).IsEqualTo(SettingState.Cleared);
        await Assert.That(result.Value).IsNull();
    }
}
