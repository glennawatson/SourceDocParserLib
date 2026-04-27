// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.NuGet.Readers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="NuGetServiceIndexReader"/>: extracts the
/// <c>PackageBaseAddress/3.0.0</c> resource (the flat-container
/// endpoint) from a v3 service-index document, ensures the result
/// always carries a trailing slash, and skips malformed or
/// non-matching resources without throwing.
/// </summary>
public class NuGetServiceIndexReaderTests
{
    /// <summary>A well-formed index returns the flat-container URL with a trailing slash.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractsFlatContainerEndpoint()
    {
        const string json = """
            {
              "version": "3.0.0",
              "resources": [
                {
                  "@id": "https://api.nuget.org/v3/registration5-gz-semver2/",
                  "@type": "RegistrationsBaseUrl/3.6.0"
                },
                {
                  "@id": "https://api.nuget.org/v3-flatcontainer/",
                  "@type": "PackageBaseAddress/3.0.0"
                }
              ]
            }
            """;

        var url = NuGetServiceIndexReader.ReadFlatContainerUrl(Encoding.UTF8.GetBytes(json));

        await Assert.That(url).IsEqualTo("https://api.nuget.org/v3-flatcontainer/");
    }

    /// <summary>An @id without a trailing slash gets one appended.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendsTrailingSlashWhenMissing()
    {
        const string json = """
            {
              "resources": [
                {
                  "@id": "https://example.org/v3-flatcontainer",
                  "@type": "PackageBaseAddress/3.0.0"
                }
              ]
            }
            """;

        var url = NuGetServiceIndexReader.ReadFlatContainerUrl(Encoding.UTF8.GetBytes(json));

        await Assert.That(url).IsEqualTo("https://example.org/v3-flatcontainer/");
    }

    /// <summary>An index without a PackageBaseAddress resource returns null.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReturnsNullWhenNoFlatContainer()
    {
        const string json = """
            {
              "resources": [
                { "@id": "https://example.org/reg/", "@type": "RegistrationsBaseUrl/3.6.0" }
              ]
            }
            """;

        var url = NuGetServiceIndexReader.ReadFlatContainerUrl(Encoding.UTF8.GetBytes(json));

        await Assert.That(url).IsNull();
    }

    /// <summary>An index without a <c>resources</c> array returns null.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReturnsNullWhenResourcesMissing()
    {
        const string json = """{ "version": "3.0.0" }""";

        var url = NuGetServiceIndexReader.ReadFlatContainerUrl(Encoding.UTF8.GetBytes(json));

        await Assert.That(url).IsNull();
    }

    /// <summary>Resources with the wrong shape (missing @id, blank @id, wrong @type) are skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SkipsMalformedResources()
    {
        const string json = """
            {
              "resources": [
                { "@type": "PackageBaseAddress/3.0.0" },
                { "@id": "   ", "@type": "PackageBaseAddress/3.0.0" },
                { "@id": 123, "@type": "PackageBaseAddress/3.0.0" },
                { "@id": "https://later.example.org/flat/", "@type": "PackageBaseAddress/3.0.0" }
              ]
            }
            """;

        var url = NuGetServiceIndexReader.ReadFlatContainerUrl(Encoding.UTF8.GetBytes(json));

        await Assert.That(url).IsEqualTo("https://later.example.org/flat/");
    }

    /// <summary>The async stream overload mirrors the byte-buffer overload's behaviour.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AsyncStreamOverloadReturnsSameValue()
    {
        const string json = """
            {
              "resources": [
                { "@id": "https://example.org/flat/", "@type": "PackageBaseAddress/3.0.0" }
              ]
            }
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var url = await NuGetServiceIndexReader.ReadFlatContainerUrlAsync(stream);

        await Assert.That(url).IsEqualTo("https://example.org/flat/");
    }

    /// <summary>Null stream argument throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AsyncStreamRejectsNull()
    {
        await Assert.That(() => NuGetServiceIndexReader.ReadFlatContainerUrlAsync(null!))
            .Throws<ArgumentNullException>();
    }
}
