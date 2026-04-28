// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests.SourceLink;

/// <summary>
/// Direct coverage of <see cref="SourceLinkJsonParser"/>: pinpoints
/// each branch the SamplePdb integration tests can't reach -- non-
/// object roots, missing / wrong-typed <c>documents</c> property,
/// malformed entries (empty key, non-string value, blank value),
/// and the wildcard / exact-match shape decision. Synthetic JSON
/// keeps each rule on its own line so a regression surfaces with a
/// focused failure.
/// </summary>
public class SourceLinkJsonParserTests
{
    /// <summary>A wildcard pattern (both sides end in <c>*</c>) emits a wildcard entry with the asterisks stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WildcardPatternStripsTrailingAsterisks()
    {
        var entries = ParseEntries("""
            { "documents": { "/local/repo/*": "https://example.org/raw/main/*" } }
            """);

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].LocalPrefix).IsEqualTo("/local/repo/");
        await Assert.That(entries[0].UrlPrefix).IsEqualTo("https://example.org/raw/main/");
        await Assert.That(entries[0].IsWildcard).IsTrue();
    }

    /// <summary>An entry without trailing asterisks is treated as an exact-match substitution.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonWildcardPatternIsExactMatch()
    {
        var entries = ParseEntries("""
            { "documents": { "/local/Foo.cs": "https://example.org/Foo.cs" } }
            """);

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].IsWildcard).IsFalse();
        await Assert.That(entries[0].LocalPrefix).IsEqualTo("/local/Foo.cs");
        await Assert.That(entries[0].UrlPrefix).IsEqualTo("https://example.org/Foo.cs");
    }

    /// <summary>An asterisk on only one side falls back to exact-match (both sides have to opt in).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AsymmetricAsteriskFallsBackToExactMatch()
    {
        var entries = ParseEntries("""
            { "documents": { "/local/*": "https://example.org/Foo.cs" } }
            """);

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].IsWildcard).IsFalse();
    }

    /// <summary>Multiple entries are returned in declaration order -- first-match-wins later in the resolver.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EntriesPreserveDeclarationOrder()
    {
        var entries = ParseEntries("""
            {
              "documents": {
                "/a/*": "https://x/a/*",
                "/b/*": "https://x/b/*",
                "/c/*": "https://x/c/*"
              }
            }
            """);

        await Assert.That(entries.Count).IsEqualTo(3);
        await Assert.That(entries[0].LocalPrefix).IsEqualTo("/a/");
        await Assert.That(entries[1].LocalPrefix).IsEqualTo("/b/");
        await Assert.That(entries[2].LocalPrefix).IsEqualTo("/c/");
    }

    /// <summary>A root that isn't a JSON object yields no entries (empty enumeration).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonObjectRootYieldsNoEntries()
    {
        var entries = ParseEntries("[1, 2, 3]");

        await Assert.That(entries.Count).IsEqualTo(0);
    }

    /// <summary>Missing <c>documents</c> property yields no entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MissingDocumentsPropertyYieldsNoEntries()
    {
        var entries = ParseEntries("""{ "version": "1.0" }""");

        await Assert.That(entries.Count).IsEqualTo(0);
    }

    /// <summary><c>documents</c> with a non-object value (e.g. an array) yields no entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonObjectDocumentsValueYieldsNoEntries()
    {
        var entries = ParseEntries("""{ "documents": [1, 2, 3] }""");

        await Assert.That(entries.Count).IsEqualTo(0);
    }

    /// <summary>A non-string value for an entry is skipped without throwing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonStringValueIsSkipped()
    {
        var entries = ParseEntries("""
            {
              "documents": {
                "/skip/me/*": 123,
                "/keep/me/*": "https://x/*"
              }
            }
            """);

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].LocalPrefix).IsEqualTo("/keep/me/");
    }

    /// <summary>A blank string value is skipped -- the parser requires a non-empty URL pattern.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BlankStringValueIsSkipped()
    {
        var entries = ParseEntries("""
            {
              "documents": {
                "/skip/*": "",
                "/keep/*": "https://x/*"
              }
            }
            """);

        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].LocalPrefix).IsEqualTo("/keep/");
    }

    /// <summary>An empty <c>documents</c> object yields no entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyDocumentsObjectYieldsNoEntries()
    {
        var entries = ParseEntries("""{ "documents": {} }""");

        await Assert.That(entries.Count).IsEqualTo(0);
    }

    /// <summary>Materialises the lazy enumeration with a UTF-8 round-trip.</summary>
    /// <param name="json">JSON document to parse.</param>
    /// <returns>The materialised entries.</returns>
    private static List<SourceLinkMapEntry> ParseEntries(string json) =>
        [.. SourceLinkJsonParser.Parse(Encoding.UTF8.GetBytes(json))];
}
