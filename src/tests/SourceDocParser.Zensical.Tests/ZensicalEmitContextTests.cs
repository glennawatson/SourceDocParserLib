// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Frozen;
using SourceDocParser.XmlDoc;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Direct tests for <see cref="ZensicalEmitContext"/> covering its
/// constructor null-guards, property wiring, and the
/// <c>RenderDocFragment</c> guard that short-circuits null/empty
/// fragments before hitting the bundled converter.
/// </summary>
public class ZensicalEmitContextTests
{
    /// <summary>Shared empty <see cref="FrozenSet{T}"/> used for the emit-context constructor tests.</summary>
    private static readonly FrozenSet<string> EmptyEmittedUids = [];

    /// <summary>The constructor wires every dependency through to its matching property.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorAssignsProperties()
    {
        var options = ZensicalEmitterOptions.Default;
        var indexes = ZensicalCatalogIndexes.Empty;
        var emittedUids = EmptyEmittedUids;
        var converter = new XmlDocToMarkdown();

        var context = new ZensicalEmitContext(options, indexes, emittedUids, converter);

        await Assert.That(context.Options).IsSameReferenceAs(options);
        await Assert.That(context.Indexes).IsSameReferenceAs(indexes);
        await Assert.That(context.EmittedUids).IsSameReferenceAs(emittedUids);
        await Assert.That(context.Converter).IsSameReferenceAs(converter);
    }

    /// <summary>A null <c>options</c> argument is rejected with <see cref="ArgumentNullException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorThrowsWhenOptionsNull()
    {
        await Assert.That(() => new ZensicalEmitContext(
            null!,
            ZensicalCatalogIndexes.Empty,
            EmptyEmittedUids,
            new XmlDocToMarkdown()))
            .Throws<ArgumentNullException>();
    }

    /// <summary>A null <c>indexes</c> argument is rejected with <see cref="ArgumentNullException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorThrowsWhenIndexesNull()
    {
        await Assert.That(() => new ZensicalEmitContext(
            ZensicalEmitterOptions.Default,
            null!,
            [],
            new XmlDocToMarkdown()))
            .Throws<ArgumentNullException>();
    }

    /// <summary>A null <c>emittedUids</c> argument is rejected with <see cref="ArgumentNullException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorThrowsWhenEmittedUidsNull()
    {
        await Assert.That(() => new ZensicalEmitContext(
            ZensicalEmitterOptions.Default,
            ZensicalCatalogIndexes.Empty,
            null!,
            new XmlDocToMarkdown()))
            .Throws<ArgumentNullException>();
    }

    /// <summary>A null <c>converter</c> argument is rejected with <see cref="ArgumentNullException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorThrowsWhenConverterNull()
    {
        await Assert.That(() => new ZensicalEmitContext(
            ZensicalEmitterOptions.Default,
            ZensicalCatalogIndexes.Empty,
            [],
            null!))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Null and empty fragments short-circuit to <see cref="string.Empty"/> without invoking the converter.</summary>
    /// <param name="rawXml">Raw fragment value: null or empty string.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task RenderDocFragmentReturnsEmptyForNullOrEmpty(string? rawXml)
    {
        var context = CreateContext();

        var result = context.RenderDocFragment(rawXml);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    /// <summary>A non-empty fragment is forwarded through the bundled converter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderDocFragmentDelegatesToConverterForNonEmpty()
    {
        var context = CreateContext();

        var result = context.RenderDocFragment("<para>hello</para>");

        await Assert.That(result).Contains("hello");
    }

    /// <summary>Builds a sharable <see cref="ZensicalEmitContext"/> with default-empty dependencies for the non-guard tests.</summary>
    /// <returns>A fresh context.</returns>
    private static ZensicalEmitContext CreateContext() => new(
        ZensicalEmitterOptions.Default,
        ZensicalCatalogIndexes.Empty,
        EmptyEmittedUids,
        new XmlDocToMarkdown());
}
