// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="FallbackPackageFoldersReader"/> on the
/// fixture (one entry pointing at the dotnet SDK fallback folder)
/// plus inline XML for the clear-then-add and dedupe cases.
/// </summary>
public class FallbackPackageFoldersReaderTests
{
    /// <summary>
    /// The fixture lists one fallback folder; reader returns it.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadsFallbackFolderFromFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-creds", "nuget.config");

        var result = await FallbackPackageFoldersReader.ReadAsync(path).ConfigureAwait(false);

        await Assert.That(result.ClearedSeen).IsFalse();
        await Assert.That(result.Folders.Length).IsEqualTo(1);
        await Assert.That(result.Folders[0]).IsEqualTo("/usr/share/dotnet/NuGetFallbackFolder");
    }

    /// <summary>
    /// Clear inside the section wipes the within-file accumulator
    /// and reports <c>ClearedSeen = true</c> so the discovery walk
    /// stops at this file.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClearWipesPriorAndIsReported()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <fallbackPackageFolders>
                <add key="before" value="/before" />
                <clear />
                <add key="after" value="/after" />
              </fallbackPackageFolders>
            </configuration>
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var result = await FallbackPackageFoldersReader.ReadAsync(stream).ConfigureAwait(false);

        await Assert.That(result.ClearedSeen).IsTrue();
        await Assert.That(result.Folders.Length).IsEqualTo(1);
        await Assert.That(result.Folders[0]).IsEqualTo("/after");
    }

    /// <summary>
    /// Duplicate keys within a file are dropped — first add wins.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FirstAddWinsForDuplicateKey()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <fallbackPackageFolders>
                <add key="dotnet-sdk" value="/first" />
                <add key="dotnet-sdk" value="/second" />
              </fallbackPackageFolders>
            </configuration>
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var result = await FallbackPackageFoldersReader.ReadAsync(stream).ConfigureAwait(false);

        await Assert.That(result.Folders.Length).IsEqualTo(1);
        await Assert.That(result.Folders[0]).IsEqualTo("/first");
    }
}
