// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical;

namespace SourceDocParser.IntegrationTests;

/// <summary>
/// End-to-end integration test that runs the full pipeline against the
/// slim debug <c>nuget-packages.json</c> fixture (3 owner-discovered
/// packages, no large owner search). Touches the network on first run
/// to populate the local NuGet cache; subsequent runs reuse the cache.
/// </summary>
public class EndToEndPipelineTests
{
    /// <summary>
    /// Fetches the fixture packages, walks them through
    /// <see cref="MetadataExtractor"/>, and asserts the catalog has
    /// real types + emitted markdown pages on disk.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunsFullPipelineAgainstFixtureConfig()
    {
        using var scratch = new ScratchDirectory("sdp-int");
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "nuget-packages.json"),
            Path.Combine(scratch.Path, "nuget-packages.json"));

        var apiPath = Path.Combine(scratch.Path, "api");
        var output = Path.Combine(scratch.Path, "out");
        Directory.CreateDirectory(apiPath);

        var source = new NuGetAssemblySource(scratch.Path, apiPath);
        var emitter = new ZensicalDocumentationEmitter();
        var extractor = new MetadataExtractor();

        var result = await extractor.RunAsync(source, output, emitter);

        await Assert.That(result.CanonicalTypes).IsGreaterThan(0);
        await Assert.That(result.PagesEmitted).IsGreaterThan(0);
        await Assert.That(result.LoadFailures).IsEqualTo(0);
        await Assert.That(Directory.EnumerateFiles(output, "*.md", SearchOption.AllDirectories).Any()).IsTrue();
    }
}
