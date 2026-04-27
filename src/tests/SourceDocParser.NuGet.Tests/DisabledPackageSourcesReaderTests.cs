// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.NuGet.Readers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="DisabledPackageSourcesReader"/> against the
/// with-creds fixture and inline canned XML for the off / non-true
/// branches.
/// </summary>
public class DisabledPackageSourcesReaderTests
{
    /// <summary>
    /// The fixture marks <c>legacy-feed</c> as disabled — the
    /// reader returns it in the result set.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadsDisabledKeysFromFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-creds", "nuget.config");

        var disabled = await DisabledPackageSourcesReader.ReadAsync(path).ConfigureAwait(false);

        await Assert.That(disabled).Contains("legacy-feed");
    }

    /// <summary>
    /// Entries whose <c>value</c> isn't the literal <c>true</c>
    /// don't get reported — disabled is opt-in only.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IgnoresEntriesWithNonTrueValue()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <disabledPackageSources>
                <add key="enabled-source" value="false" />
                <add key="empty-value" value="" />
                <add key="real-disable" value="true" />
              </disabledPackageSources>
            </configuration>
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var disabled = await DisabledPackageSourcesReader.ReadAsync(stream).ConfigureAwait(false);

        await Assert.That(disabled.Count).IsEqualTo(1);
        await Assert.That(disabled).Contains("real-disable");
    }
}
