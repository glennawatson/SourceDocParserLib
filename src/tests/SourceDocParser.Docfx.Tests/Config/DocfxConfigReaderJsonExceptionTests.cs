// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using System.Text.Json;
using SourceDocParser.Docfx.Config;

namespace SourceDocParser.Docfx.Tests.Config;

/// <summary>
/// Pins the wrong-shape branches of <see cref="DocfxConfigReader.Read"/>:
/// each <c>throw new JsonException</c> guard fires when the
/// corresponding array entry isn't an object.
/// </summary>
public class DocfxConfigReaderJsonExceptionTests
{
    /// <summary>A non-object metadata entry throws with a clear message.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonObjectMetadataEntryThrows() =>
        await Assert.That(() => Parse("""{ "metadata": [ "not-an-object" ] }""")).Throws<JsonException>();

    /// <summary>A non-object src entry inside metadata throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonObjectMetadataSrcEntryThrows() =>
        await Assert.That(() => Parse("""{ "metadata": [ { "src": [ "not-object" ] } ] }""")).Throws<JsonException>();

    /// <summary>A non-object content entry inside build throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonObjectBuildContentEntryThrows() =>
        await Assert.That(() => Parse("""{ "build": { "content": [ "not-object" ] } }""")).Throws<JsonException>();

    /// <summary>An empty src array on a metadata entry yields an empty source list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MissingMetadataSrcArrayYieldsEmptySources()
    {
        var config = Parse("""{ "metadata": [ {} ] }""");

        await Assert.That(config.Metadata.Length).IsEqualTo(1);
        await Assert.That(config.Metadata[0].Src).IsEmpty();
    }

    /// <summary>A missing build content array yields an empty content list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MissingBuildContentArrayYieldsEmptyList()
    {
        var config = Parse("""{ "build": { } }""");

        await Assert.That(config.Build.Content).IsEmpty();
    }

    /// <summary>Reads <paramref name="json"/> through the public entry point.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The parsed config.</returns>
    private static DocfxConfig Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return DocfxConfigReader.Read(stream);
    }
}
