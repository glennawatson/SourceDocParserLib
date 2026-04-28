// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the public surface of <see cref="TypePageEmitter"/> -- heading
/// label, file path layout, kind-specific section presence -- so any
/// future change to the markdown shape surfaces here before it lands
/// in a downstream emitter consumer.
/// </summary>
public class TypePageEmitterTests
{
    /// <summary>Class types render an H1 heading that includes the kind keyword.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderProducesHeadingForClass()
    {
        var page = TypePageEmitter.Render(TestData.ObjectType("Foo"));

        await Assert.That(page).Contains("# Foo class");
    }

    /// <summary>Enum types render the values table inline on the type page.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsValueTableForEnum()
    {
        var values = new List<ApiEnumValue>
        {
            new("Red", "F:Color.Red", "0", ApiDocumentation.Empty, null),
            new("Green", "F:Color.Green", "1", ApiDocumentation.Empty, null),
        };
        var enumType = TestData.EnumType("Color") with { Values = [.. values] };

        var page = TypePageEmitter.Render(enumType);

        await Assert.That(page).Contains("# Color enum");
        await Assert.That(page).Contains("## Values");
        await Assert.That(page).Contains("`Red`");
        await Assert.That(page).Contains("`Green`");
    }

    /// <summary>
    /// Delegate types render the Invoke signature under a Signature
    /// section, not as a member page.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsSignatureSectionForDelegate()
    {
        var page = TypePageEmitter.Render(TestData.DelegateType("Handler"));

        await Assert.That(page).Contains("# Handler delegate");
        await Assert.That(page).Contains("## Signature");
    }

    /// <summary>
    /// Default path layout: <c>Assembly/Namespace/Type.md</c> for
    /// namespaced types and <c>Assembly/_global/Type.md</c> for global-
    /// namespace types. The assembly-name folder lets multi-package
    /// sites keep clashing namespaces (e.g. <c>ReactiveUI</c> in both
    /// <c>ReactiveUI</c> and <c>ReactiveUI.Wpf</c>) cleanly separated.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForUsesNamespaceTreeAndMarkdownExtension()
    {
        var withNamespace = TestData.ObjectType("Foo") with { Namespace = "My.Lib" };
        var globalNs = TestData.ObjectType("Bar");

        await Assert.That(TypePageEmitter.PathFor(withNamespace)).IsEqualTo("Test/My/Lib/Foo.md");
        await Assert.That(TypePageEmitter.PathFor(globalNs)).IsEqualTo("Test/_global/Bar.md");
    }

    /// <summary>
    /// Generic types use curly braces in the file stem so the path stays
    /// safe on Windows and readable in URLs.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForReplacesAngleBracketsWithCurlyBraces()
    {
        var generic = TestData.ObjectType("List") with { Arity = 1 };

        var path = TypePageEmitter.PathFor(generic);

        await Assert.That(path).EndsWith("List{T}.md");
    }

    /// <summary>Render rejects null types with the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderRejectsNullType()
    {
        await Assert.That(Act).Throws<ArgumentNullException>();

        static string Act() => TypePageEmitter.Render(null!);
    }

    /// <summary>PathFor rejects null types with the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForRejectsNullType()
    {
        await Assert.That(Act).Throws<ArgumentNullException>();

        static string Act() => TypePageEmitter.PathFor(null!);
    }

    /// <summary>
    /// When a member summary exceeds the table-cell length and has no
    /// space within the word-boundary window (first half of the limit),
    /// the renderer falls back to a hard cut at the maximum length plus
    /// an ellipsis. Pins the otherwise-uncovered hard-cut branch in
    /// <c>OneLineSummary</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderTruncatesLongUnbrokenSummaryWithHardCut()
    {
        // A 250-char run with the only space placed past the half-limit
        // boundary so LastIndexOf(' ', 199) returns a position not greater
        // than MinimumSummaryWordBoundary (=100).
        var noEarlySpace = new string('a', 95) + new string('b', 155) + " end";
        var summaryDoc = ApiDocumentation.Empty with { Summary = noEarlySpace };
        var member = new ApiMember(
            Name: "DoThing",
            Uid: "M:Foo.DoThing",
            Kind: ApiMemberKind.Method,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "void DoThing()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:Foo",
            ContainingTypeName: "Foo",
            SourceUrl: null,
            Documentation: summaryDoc,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var page = TypePageEmitter.Render(type);

        await Assert.That(page).Contains("...");
        await Assert.That(page).DoesNotContain(noEarlySpace);
    }
}
