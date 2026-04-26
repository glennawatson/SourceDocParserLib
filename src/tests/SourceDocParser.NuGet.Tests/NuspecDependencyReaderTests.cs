// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins each helper in <see cref="NuspecDependencyReader"/> against
/// canned nuspec XML — covers the schema namespaces we recognise,
/// the per-TFM-group dedupe, the root-only nuspec rule, and the
/// end-to-end nupkg-zip path.
/// </summary>
public class NuspecDependencyReaderTests
{
    /// <summary>
    /// Reads a typical multi-group nuspec — one dependency in each
    /// of two TFM groups, plus a duplicate in a third group. The
    /// returned set should have one entry per distinct package id.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDependencyIdsDedupesAcrossTfmGroups()
    {
        const string nuspecXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata>
                <id>Splat</id>
                <version>19.3.1</version>
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="Splat.Core" version="19.3.1" />
                    <dependency id="Splat.Logging" version="19.3.1" />
                  </group>
                  <group targetFramework="net9.0">
                    <dependency id="Splat.Core" version="19.3.1" />
                    <dependency id="Splat.Builder" version="19.3.1" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;

        var ids = await NuspecDependencyReader.ReadDependencyIdsAsync(StreamFor(nuspecXml)).ConfigureAwait(false);

        await Assert.That(ids.Count).IsEqualTo(3);
        await Assert.That(ids).Contains("Splat.Core");
        await Assert.That(ids).Contains("Splat.Logging");
        await Assert.That(ids).Contains("Splat.Builder");
    }

    /// <summary>
    /// A nuspec with no <c>&lt;dependencies&gt;</c> element returns
    /// an empty set rather than throwing — package leaves of the
    /// dep graph are common (System.Reactive bottoms out).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDependencyIdsReturnsEmptyWhenNoDeps()
    {
        const string nuspecXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata>
                <id>Leaf</id>
                <version>1.0.0</version>
              </metadata>
            </package>
            """;

        var ids = await NuspecDependencyReader.ReadDependencyIdsAsync(StreamFor(nuspecXml)).ConfigureAwait(false);

        await Assert.That(ids.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Splat 19.x and other recent ReactiveUI packages ship with the
    /// 2013/05 nuspec namespace — that's the schema bump the original
    /// allow-list missed and the regression that left
    /// <c>Splat.Core</c> / <c>Splat.Logging</c> / <c>Splat.Builder</c>
    /// out of the transitive fetch.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDependencyIdsRecognises2013Namespace()
    {
        const string nuspecXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>Splat</id>
                <version>19.3.1</version>
                <dependencies>
                  <group targetFramework="net10.0">
                    <dependency id="Splat.Builder" version="19.3.1" exclude="Build,Analyzers" />
                    <dependency id="Splat.Logging" version="19.3.1" exclude="Build,Analyzers" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;

        var ids = await NuspecDependencyReader.ReadDependencyIdsAsync(StreamFor(nuspecXml)).ConfigureAwait(false);

        await Assert.That(ids).Contains("Splat.Builder");
        await Assert.That(ids).Contains("Splat.Logging");
    }

    /// <summary>
    /// Older nuspec namespace (2011/08) is recognised — we still see
    /// these on legacy packages that haven't been re-published since
    /// the schema changed.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDependencyIdsRecognisesLegacyNamespace()
    {
        const string nuspecXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd">
              <metadata>
                <id>Old</id>
                <version>1.0.0</version>
                <dependencies>
                  <dependency id="Old.Core" version="1.0.0" />
                </dependencies>
              </metadata>
            </package>
            """;

        var ids = await NuspecDependencyReader.ReadDependencyIdsAsync(StreamFor(nuspecXml)).ConfigureAwait(false);

        await Assert.That(ids).Contains("Old.Core");
    }

    /// <summary>
    /// Foreign XML files dropped into a nupkg aren't allowed to
    /// poison the dependency set — only elements declared under a
    /// recognised nuspec namespace count as dependency entries.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDependencyIdsIgnoresUnrelatedXml()
    {
        const string nuspecXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <foreign xmlns="urn:not-nuspec">
              <dependency id="ShouldNotAppear" />
            </foreign>
            """;

        var ids = await NuspecDependencyReader.ReadDependencyIdsAsync(StreamFor(nuspecXml)).ConfigureAwait(false);

        await Assert.That(ids.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Whitespace-only ids skip — defensive against malformed nuspecs
    /// that would otherwise feed an empty string into the fetch queue.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDependencyIdsSkipsBlankIds()
    {
        const string nuspecXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata>
                <id>Test</id>
                <version>1.0.0</version>
                <dependencies>
                  <dependency id="" version="1.0.0" />
                  <dependency id="   " version="1.0.0" />
                  <dependency id="Real" version="1.0.0" />
                </dependencies>
              </metadata>
            </package>
            """;

        var ids = await NuspecDependencyReader.ReadDependencyIdsAsync(StreamFor(nuspecXml)).ConfigureAwait(false);

        await Assert.That(ids.Count).IsEqualTo(1);
        await Assert.That(ids).Contains("Real");
    }

    /// <summary>
    /// Root-nuspec recogniser: case-insensitive .nuspec suffix and no
    /// path separator. Pins both the positive and negative branches.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsRootNuspecEntryFiltersByExtensionAndPath()
    {
        await Assert.That(NuspecDependencyReader.IsRootNuspecEntry("Splat.nuspec")).IsTrue();
        await Assert.That(NuspecDependencyReader.IsRootNuspecEntry("Splat.NuSpec")).IsTrue();
        await Assert.That(NuspecDependencyReader.IsRootNuspecEntry("nested/Splat.nuspec")).IsFalse();
        await Assert.That(NuspecDependencyReader.IsRootNuspecEntry("nested\\Splat.nuspec")).IsFalse();
        await Assert.That(NuspecDependencyReader.IsRootNuspecEntry("Splat.dll")).IsFalse();
    }

    /// <summary>
    /// End-to-end through a real zip: we synthesise a one-entry
    /// .nupkg in memory, write it to disk, and assert the path-based
    /// overload reads its dependencies.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDependencyIdsFromNupkgPathOpensZipAndParses()
    {
        const string nuspecXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata>
                <id>Outer</id>
                <version>2.0.0</version>
                <dependencies>
                  <dependency id="Inner" version="2.0.0" />
                </dependencies>
              </metadata>
            </package>
            """;

        var nupkgPath = Path.Combine(Path.GetTempPath(), $"nuspec-test-{Guid.NewGuid():N}.nupkg");
        try
        {
            await using (var fs = File.Create(nupkgPath))
            await using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("Outer.nuspec");
                await using var stream = await entry.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                await using var writer = new StreamWriter(stream, Encoding.UTF8);
                await writer.WriteAsync(nuspecXml).ConfigureAwait(false);
            }

            var ids = await NuspecDependencyReader.ReadDependencyIdsAsync(nupkgPath).ConfigureAwait(false);

            await Assert.That(ids).Contains("Inner");
        }
        finally
        {
            if (File.Exists(nupkgPath))
            {
                File.Delete(nupkgPath);
            }
        }
    }

    /// <summary>IsDependencyElement: positive cases for each recognised namespace + bare local name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsDependencyElementMatchesEachRecognisedNamespace()
    {
        const string xml = """
            <root xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <dependency id="X" />
            </root>
            """;
        using var reader = XmlReader.Create(StreamFor(xml), new XmlReaderSettings { Async = true });
        await AdvanceToFirstElementWithLocalName(reader, "dependency");
        await Assert.That(NuspecDependencyReader.IsDependencyElement(reader)).IsTrue();
    }

    /// <summary>IsDependencyElement: rejects elements whose local name doesn't match.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsDependencyElementRejectsOtherLocalNames()
    {
        const string xml = """
            <root xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata id="X" />
            </root>
            """;
        using var reader = XmlReader.Create(StreamFor(xml), new XmlReaderSettings { Async = true });
        await AdvanceToFirstElementWithLocalName(reader, "metadata");
        await Assert.That(NuspecDependencyReader.IsDependencyElement(reader)).IsFalse();
    }

    /// <summary>Wraps <paramref name="content"/> in a UTF-8 MemoryStream for the reader.</summary>
    /// <param name="content">XML text.</param>
    /// <returns>A seekable stream positioned at offset zero.</returns>
    private static MemoryStream StreamFor(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    /// <summary>Advances <paramref name="reader"/> to the first element whose local name matches <paramref name="localName"/>.</summary>
    /// <param name="reader">Reader to drive.</param>
    /// <param name="localName">Local name to land on.</param>
    /// <returns>A task representing the navigation.</returns>
    private static async Task AdvanceToFirstElementWithLocalName(XmlReader reader, string localName)
    {
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == localName)
            {
                return;
            }
        }

        throw new InvalidOperationException($"Element <{localName}> not found.");
    }
}
