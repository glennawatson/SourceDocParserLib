// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical;

namespace SourceDocParser.IntegrationTests;

/// <summary>
/// Pins the page-emission contract on a deliberately heavy fixture
/// (UI-control + icon-font enum packages from CrissCross.*) so the
/// total pages-per-type ratio stays inside the bound the contract
/// promises. Catches walker or emitter changes that re-introduce
/// per-enum-value or per-delegate-overload pages.
/// </summary>
public class LargeAssemblyEmitterPageBoundsTests
{
    /// <summary>
    /// Runs the pipeline against the heavy fixture and asserts the
    /// total emitted page count stays within a bounded multiple of
    /// the canonical type count. Also writes a top-N diagnostic so
    /// regressions surface the worst-offender type immediately.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PageCountStaysBoundedRelativeToTypeCount()
    {
        using var scratch = new ScratchDirectory("sdp-pagebounds");
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "nuget-packages-page-bounds.json"),
            Path.Combine(scratch.Path, "nuget-packages.json"));

        var apiPath = Path.Combine(scratch.Path, "api");
        var output = Path.Combine(scratch.Path, "out");
        Directory.CreateDirectory(apiPath);

        var source = new NuGetAssemblySource(scratch.Path, apiPath);
        var emitter = new ZensicalDocumentationEmitter();
        var extractor = new MetadataExtractor();

        var result = await extractor.RunAsync(source, output, emitter);

        var emittedFiles = 0;
        foreach (var unused in Directory.EnumerateFiles(output, "*.md", SearchOption.AllDirectories))
        {
            emittedFiles++;
        }

        // Each type-page sits next to a directory of the same stem
        // holding its per-overload-group member pages; counting the
        // files in those sibling directories tells us which types are
        // contributing the long tail.
        List<(string dir, int count)> perTypeCounts = [];
        foreach (var dir in Directory.EnumerateDirectories(output, "*", SearchOption.AllDirectories))
        {
            var count = 0;
            foreach (var unused in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
            {
                count++;
            }

            if (count > 0)
            {
                perTypeCounts.Add((dir, count));
            }
        }

        perTypeCounts.Sort(static (a, b) => b.count.CompareTo(a.count));

        if (perTypeCounts.Count > 20)
        {
            perTypeCounts.RemoveRange(20, perTypeCounts.Count - 20);
        }

        Console.WriteLine($"Pipeline: {result.CanonicalTypes} canonical types, {emittedFiles} pages " +
            $"(ratio {(double)emittedFiles / result.CanonicalTypes:F1}x). Top contributors:");
        foreach (var (dir, count) in perTypeCounts)
        {
            Console.WriteLine($"  {count,5} pages in {Path.GetRelativePath(output, dir)}");
        }

        // 1 type page + N overload-group pages. ~30 distinct member
        // names covers wide WPF control classes while flagging any
        // regression that lets per-enum-value or per-delegate-overload
        // pages back in (those would push the ratio into the hundreds).
        const int MaxPagesPerType = 30;
        var allowed = result.CanonicalTypes * MaxPagesPerType;

        await Assert.That(result.CanonicalTypes).IsGreaterThan(0);
        await Assert.That(result.PagesEmitted).IsEqualTo(emittedFiles);
        await Assert.That(result.LoadFailures).IsEqualTo(0);
        await Assert.That(emittedFiles)
            .IsLessThan(allowed)
            .Because($"emitter produced {emittedFiles} pages from {result.CanonicalTypes} canonical types " +
                $"(ratio {(double)emittedFiles / result.CanonicalTypes:F1}x); cap is {MaxPagesPerType}x = {allowed}.");
    }
}
