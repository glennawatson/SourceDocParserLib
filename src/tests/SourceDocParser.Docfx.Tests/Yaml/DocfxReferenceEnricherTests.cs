// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the docfx reference-enricher: BCL types route to Microsoft
/// Learn URLs and carry <c>isExternal: true</c>; types in the
/// internal-uid set link to a local <c>.html</c> page; constructed
/// generics emit a <c>spec.csharp</c> token list and a
/// <c>definition</c> back-pointer to their open-generic UID.
/// </summary>
public class DocfxReferenceEnricherTests
{
    /// <summary>BCL primitive class refs lower to their C# keyword form on the rendered name fields.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BclPrimitiveClassReferenceLowersToKeyword()
    {
        var page = RenderPageWithReference(new ApiTypeReference("Object", "T:System.Object"), internalUids: []);

        await Assert.That(page).Contains("  name: object");
        await Assert.That(page).Contains("  nameWithType: object");
        await Assert.That(page).Contains("  fullName: object");
    }

    /// <summary>BCL refs get isExternal + Microsoft Learn href + parent.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BclReferenceRoutesToMicrosoftLearn()
    {
        var page = RenderPageWithReference(new ApiTypeReference("Object", "T:System.Object"), internalUids: []);

        await Assert.That(page).Contains("- uid: System.Object");
        await Assert.That(page).Contains("  parent: System");
        await Assert.That(page).Contains("  isExternal: true");
        await Assert.That(page).Contains("  href: https://learn.microsoft.com/dotnet/api/system.object");
    }

    /// <summary>Internal references link to the local html page and skip isExternal.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InternalReferenceLinksToLocalPage()
    {
        var page = RenderPageWithReference(
            new ApiTypeReference("IFoo", "T:My.IFoo"),
            internalUids: ["T:My.IFoo"]);

        await Assert.That(page).Contains("- uid: My.IFoo");
        await Assert.That(page).Contains("  href: My.IFoo.html");
        await Assert.That(page).DoesNotContain("isExternal: true\n  parent: My");
    }

    /// <summary>spec.csharp components always carry <c>isExternal: true</c>, even when the open-generic target is in our walk set.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SpecCsharpComponentsAlwaysCarryIsExternal()
    {
        // Open-generic IFoo`1 IS in the internal set yet docfx still
        // emits `isExternal: true` on the spec.csharp entry — spec
        // components are always referenced as a separate page entry.
        var page = RenderPageWithReference(
            new ApiTypeReference("IFoo<int>", "T:My.IFoo{System.Int32}"),
            internalUids: ["T:My.IFoo`1"]);

        await Assert.That(page).Contains("spec.csharp:");
        await Assert.That(page).Contains("  - uid: My.IFoo`1");

        // Pin the field appears under the open-generic spec entry.
        var specBlockStart = page.IndexOf("spec.csharp:", StringComparison.Ordinal);
        var afterOpen = page.IndexOf("  - uid: My.IFoo`1", specBlockStart, StringComparison.Ordinal);
        var afterIsExternal = page.IndexOf("isExternal: true", afterOpen, StringComparison.Ordinal);
        var nextSpecEntry = page.IndexOf("\n  - ", afterOpen + 1, StringComparison.Ordinal);

        await Assert.That(afterIsExternal).IsGreaterThan(afterOpen);
        await Assert.That(afterIsExternal).IsLessThan(nextSpecEntry);
    }

    /// <summary>Constructed generics emit a spec.csharp token list and a definition back-pointer.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GenericReferenceEmitsSpecCsharp()
    {
        var page = RenderPageWithReference(
            new ApiTypeReference("IObservable<int>", "T:System.IObservable{System.Int32}"),
            internalUids: []);

        await Assert.That(page).Contains("definition: System.IObservable`1");
        await Assert.That(page).Contains("spec.csharp:");
        await Assert.That(page).Contains("  - uid: System.IObservable`1");
        await Assert.That(page).Contains("  - name: <");
        await Assert.That(page).Contains("  - name: '>'");
    }

    /// <summary>
    /// Convenience: render a Foo type whose only reference is
    /// <paramref name="reference"/>, then return the YAML page.
    /// </summary>
    /// <param name="reference">Reference to enrich.</param>
    /// <param name="internalUids">UIDs treated as internal.</param>
    /// <returns>The full YAML page text.</returns>
    private static string RenderPageWithReference(ApiTypeReference reference, HashSet<string> internalUids)
    {
        var sb = new StringBuilder();
        DocfxReferenceEnricher.AppendEnrichedReference(sb, reference, internalUids);
        return sb.ToString();
    }
}
