// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the union-type rendering path of <see cref="TypePageEmitter"/>:
/// the cases table, the mermaid composition diagram, and the kind
/// label for unions.
/// </summary>
public class UnionTypePageEmitterTests
{
    /// <summary>A union type page renders the cases table and mermaid diagram.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderUnionEmitsCasesAndDiagram()
    {
        ApiTypeReference[] cases =
        [
            new("Circle", "T:My.Shape.Circle"),
            new("Square", "T:My.Shape.Square"),
        ];
        var union = new ApiUnionType(
            Name: "Shape",
            FullName: "My.Shape",
            Uid: "T:My.Shape",
            Namespace: "My",
            Arity: 0,
            IsStatic: false,
            IsSealed: false,
            IsAbstract: true,
            AssemblyName: "My",
            Documentation: ApiDocumentation.Empty,
            BaseType: null,
            Interfaces: [],
            SourceUrl: null,
            AppliesTo: [],
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: [],
            Members: [],
            Cases: [.. cases]);

        var page = TypePageEmitter.Render(union);

        await Assert.That(page).Contains("## Cases");
        await Assert.That(page).Contains("| Case |");
        await Assert.That(page).Contains("classDiagram");
        await Assert.That(page).Contains("<<union>>");
        await Assert.That(page).Contains("Circle");
        await Assert.That(page).Contains("Square");
    }
}
