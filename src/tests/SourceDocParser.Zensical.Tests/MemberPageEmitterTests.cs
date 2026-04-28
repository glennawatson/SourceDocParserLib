// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the public surface of <see cref="MemberPageEmitter"/> -- what
/// the per-overload-group page looks like, where it lands on disk,
/// and how it handles overload buckets that share a name.
/// </summary>
public class MemberPageEmitterTests
{
    /// <summary>
    /// A single-overload bucket renders as a member page with the
    /// member name in the heading and the signature inline.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsHeadingAndSignatureForSingleOverload()
    {
        var type = TestData.ObjectType("Foo");
        var member = NewMember("Run", "void Run()");

        var page = MemberPageEmitter.Render(type, "Run", [member]);

        await Assert.That(page).Contains("Foo.Run");
        await Assert.That(page).Contains("void Run()");
    }

    /// <summary>
    /// Multiple overloads sharing the same name all surface on the same
    /// page -- the bucket-by-name behaviour the Zensical emitter relies
    /// on to keep page count bounded.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsEverySignatureForOverloadBucket()
    {
        var type = TestData.ObjectType("Foo");
        ApiMember[] overloads =
        [
            NewMember("Run", "void Run()"),
            NewMember("Run", "void Run(int count)"),
            NewMember("Run", "void Run(int count, string label)"),
        ];

        var page = MemberPageEmitter.Render(type, "Run", overloads);

        await Assert.That(page).Contains("void Run()");
        await Assert.That(page).Contains("void Run(int count)");
        await Assert.That(page).Contains("void Run(int count, string label)");
    }

    /// <summary>
    /// Path layout: per-overload-group pages sit next to the type page
    /// inside a folder named after the type stem, using the member
    /// name as the file stem.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForUsesTypeFolderAndMemberStem()
    {
        var type = TestData.ObjectType("Foo") with { Namespace = "My.Lib" };

        var path = MemberPageEmitter.PathFor(type, "Run");

        await Assert.That(path).IsEqualTo("Test/My/Lib/Foo/Run.md");
    }

    /// <summary>
    /// Generic types use curly braces in the type folder name so the
    /// path stays cross-platform safe.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForUsesCurlyBracesForGenericTypeFolder()
    {
        var generic = TestData.ObjectType("List") with { Arity = 1 };

        var path = MemberPageEmitter.PathFor(generic, "Add");

        await Assert.That(path).EndsWith("List{T}/Add.md");
    }

    /// <summary>
    /// Member file stems replace the small set of path-hostile characters.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForSanitisesMemberStem()
    {
        var type = TestData.ObjectType("Foo");

        var path = MemberPageEmitter.PathFor(type, "Run<T>.Core:Impl");

        await Assert.That(path).EndsWith("Foo/Run{T}_Core_Impl.md");
    }

    /// <summary>Render rejects null containingType with the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderRejectsNullContainingType()
    {
        await Assert.That(Act).Throws<ArgumentNullException>();

        static string Act() => MemberPageEmitter.Render(null!, "Run", [NewMember("Run", "void Run()")]);
    }

    /// <summary>Render rejects null overload list with the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderRejectsNullOverloads()
    {
        await Assert.That(Act).Throws<ArgumentNullException>();

        static string Act() => MemberPageEmitter.Render(TestData.ObjectType("Foo"), "Run", null!);
    }

    /// <summary>PathFor rejects empty member name with the whitespace guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForRejectsEmptyMemberName()
    {
        await Assert.That(Act).Throws<ArgumentException>();

        static string Act() => MemberPageEmitter.PathFor(TestData.ObjectType("Foo"), string.Empty);
    }

    /// <summary>
    /// Plain members produce a single-level back-link to the
    /// containing type page.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsSingleLevelBackLinkForFlatMember()
    {
        var type = TestData.ObjectType("Foo");
        var member = NewMember("Run", "void Run()");

        var page = MemberPageEmitter.Render(type, "Run", [member]);

        await Assert.That(page).Contains("Type: [Foo](../Foo.md)");
    }

    /// <summary>
    /// Avalonia compiled-XAML emits members whose names contain
    /// forward slashes (e.g. <c>Build_/Themes/Index.axaml</c>). The
    /// sanitised filename keeps those slashes as folder boundaries,
    /// so the back-link to the containing type must walk up one
    /// extra <c>../</c> per slash. Hard-coding a single <c>../</c>
    /// produces broken cross-links that surface as docfx warnings.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsMultiLevelBackLinkWhenMemberNameHasSlashes()
    {
        var type = TestData.ObjectType("AvaloniaResources");
        var member = NewMember("Build_/Themes/Index.axaml", "void Build_/Themes/Index.axaml()");

        var page = MemberPageEmitter.Render(type, "Build_/Themes/Index.axaml", [member]);

        await Assert.That(page).Contains("Type: [AvaloniaResources](../../../AvaloniaResources.md)");
        await Assert.That(page).DoesNotContain("Type: [AvaloniaResources](../AvaloniaResources.md)");
    }

    /// <summary>Builds a minimal <see cref="ApiMember"/> with the supplied name and signature.</summary>
    /// <param name="name">Member name.</param>
    /// <param name="signature">Display signature.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember NewMember(string name, string signature) => new(
        Name: name,
        Uid: $"M:Foo.{name}",
        Kind: ApiMemberKind.Method,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: signature,
        Parameters: [],
        TypeParameters: [],
        ReturnType: null,
        ContainingTypeUid: "Foo",
        ContainingTypeName: "Foo",
        SourceUrl: null,
        Documentation: ApiDocumentation.Empty,
        IsObsolete: false,
        ObsoleteMessage: null,
        Attributes: []);
}
