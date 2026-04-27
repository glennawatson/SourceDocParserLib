// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="ZensicalCatalogIndexes"/> on the same shape as
/// the docfx-side bundle: empty-singleton return on misses, correct
/// grouping for derived / inherited / extension lookups, and the
/// markdown-section emit downstream when threaded through
/// <see cref="TypePageEmitter"/>.
/// </summary>
public class ZensicalCatalogIndexesTests
{
    /// <summary>Empty input returns the shared <see cref="ZensicalCatalogIndexes.Empty"/> singleton.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildEmptyReturnsSingleton()
    {
        var indexes = ZensicalCatalogIndexes.Build([]);

        await Assert.That(indexes).IsSameReferenceAs(ZensicalCatalogIndexes.Empty);
    }

    /// <summary>Misses return the shared empty array.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnknownUidReturnsEmptyArrays()
    {
        var indexes = ZensicalCatalogIndexes.Empty;

        await Assert.That(indexes.GetDerived("T:Missing")).IsEmpty();
        await Assert.That(indexes.GetExtensions("T:Missing")).IsEmpty();
        await Assert.That(indexes.GetInherited("T:Missing")).IsEmpty();
    }

    /// <summary>Derived lookup buckets each subclass under its base type uid.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DerivedLookupBucketsByBaseUid()
    {
        var baseType = TestData.ObjectType("Base");
        var sub = TestData.ObjectType("Derived") with
        {
            BaseType = new("Base", "Base"),
        };

        var indexes = ZensicalCatalogIndexes.Build([baseType, sub]);
        var derived = indexes.GetDerived("Base");

        await Assert.That(derived.Length).IsEqualTo(1);
        await Assert.That(derived[0].Uid).IsEqualTo("Derived");
    }

    /// <summary>Type page emits the new sections when the indexes carry entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypePageEmitsSectionsWhenIndexesPopulated()
    {
        var baseType = TestData.ObjectType("Base");
        var sub = TestData.ObjectType("Derived") with
        {
            BaseType = new("Base", "Base"),
        };
        var indexes = ZensicalCatalogIndexes.Build([baseType, sub]);

        var page = TypePageEmitter.Render(baseType, ZensicalEmitterOptions.Default, indexes);

        await Assert.That(page).Contains("## Derived types");
        await Assert.That(page).Contains("Derived");
        await Assert.That(page).Contains("??? abstract \"Inherited members\"");
    }

    /// <summary>Type-level seealso renders into a "See also" section.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SeeAlsoRendersOnTypePage()
    {
        var docs = ApiDocumentation.Empty with { SeeAlso = ["T:Foo.Related"] };
        var type = TestData.ObjectType("Foo") with { Documentation = docs };

        var page = TypePageEmitter.Render(type);

        await Assert.That(page).Contains("## See also");
        await Assert.That(page).Contains("Foo.Related");
    }
}
