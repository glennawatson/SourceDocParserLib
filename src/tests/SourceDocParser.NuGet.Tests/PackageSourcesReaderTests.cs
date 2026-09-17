// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Readers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="PackageSourcesReader"/> against fixture
/// <c>nuget.config</c> files plus inline canned XML for the
/// edge cases -- first-add-wins on duplicate keys, <c>clear</c>
/// resets the within-file accumulator, post-clear adds remain.
/// </summary>
public class PackageSourcesReaderTests
{
    /// <summary>Expected fixture value used by ReadsSourcesInDeclaredOrder.</summary>
    private const int ReadsSourcesInDeclaredOrderExpectedValue = 2;

    /// <summary>
    /// Reads two sources from the with-sources fixture in declared
    /// order. nuget.org always comes first because that's how the
    /// fixture lays them out.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadsSourcesInDeclaredOrder()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-sources", "nuget.config");

        var result = await PackageSourcesReader.ReadPackageSourcesAsync(path).ConfigureAwait(false);

        await Assert.That(result.ClearedSeen).IsFalse();
        await Assert.That(result.Sources.Length).IsEqualTo(ReadsSourcesInDeclaredOrderExpectedValue);
        await Assert.That(result.Sources[0].Key).IsEqualTo("nuget.org");
        await Assert.That(result.Sources[1].Key).IsEqualTo("github");
    }

    /// <summary>
    /// A file with <c>clear/</c> followed by a single add
    /// returns just that add and reports <c>ClearedSeen = true</c>
    /// so the discovery walk stops chaining to parents.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClearWipesAndPostClearAddSurvives()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sources-clear", "nuget.config");

        var result = await PackageSourcesReader.ReadPackageSourcesAsync(path).ConfigureAwait(false);

        await Assert.That(result.ClearedSeen).IsTrue();
        await Assert.That(result.Sources.Length).IsEqualTo(1);
        await Assert.That(result.Sources[0].Key).IsEqualTo("github");
    }

    /// <summary>
    /// Within a file, the FIRST <c>add</c> for a given key
    /// wins -- duplicates after it are silently dropped. Mirrors
    /// NuGet's <c>GetFirstItemWithAttribute</c> rule.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FirstAddWinsForDuplicateKey()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="nuget.org" value="https://first" />
                <add key="nuget.org" value="https://second" />
              </packageSources>
            </configuration>
            """u8.ToArray();

        await using var stream = new MemoryStream(xml);
        var result = await PackageSourcesReader.ReadPackageSourcesAsync(stream).ConfigureAwait(false);

        await Assert.That(result.Sources.Length).IsEqualTo(1);
        await Assert.That(result.Sources[0].Url).IsEqualTo("https://first");
    }

    /// <summary>Pre-clear adds get wiped; the file's effective contribution is only the post-clear adds.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClearWipesPriorAddsInSameFile()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="before-clear" value="https://before" />
                <clear />
                <add key="after-clear" value="https://after" />
              </packageSources>
            </configuration>
            """u8.ToArray();

        await using var stream = new MemoryStream(xml);
        var result = await PackageSourcesReader.ReadPackageSourcesAsync(stream).ConfigureAwait(false);

        await Assert.That(result.ClearedSeen).IsTrue();
        await Assert.That(result.Sources.Length).IsEqualTo(1);
        await Assert.That(result.Sources[0].Key).IsEqualTo("after-clear");
    }

    /// <summary>A file without a <c>packageSources</c> section returns an empty result with <c>ClearedSeen = false</c> -- discovery keeps walking.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SectionAbsentReturnsEmpty()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="/x" />
              </config>
            </configuration>
            """u8.ToArray();

        await using var stream = new MemoryStream(xml);
        var result = await PackageSourcesReader.ReadPackageSourcesAsync(stream).ConfigureAwait(false);

        await Assert.That(result.ClearedSeen).IsFalse();
        await Assert.That(result.Sources.Length).IsEqualTo(0);
    }
}
