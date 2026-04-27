// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="DocfxObjectSignature.Synthesise"/> on the docfx
/// declaration-line shape it produces for each kind / modifier
/// combination, plus the base+interface composition rules.
/// </summary>
public class DocfxObjectSignatureTests
{
    /// <summary>Bare class with no base or interfaces yields just <c>public class Name</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BareClassRendersModifierAndKind()
    {
        var sig = DocfxObjectSignature.Synthesise(TestData.ObjectType("Foo"));

        await Assert.That(sig).IsEqualTo("public class Foo");
    }

    /// <summary>Static class renders <c>public static class</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StaticClassUsesStaticModifier()
    {
        var type = TestData.ObjectType("Helpers") with { IsStatic = true };

        var sig = DocfxObjectSignature.Synthesise(type);

        await Assert.That(sig).IsEqualTo("public static class Helpers");
    }

    /// <summary>Sealed class renders <c>public sealed class</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SealedClassUsesSealedModifier()
    {
        var type = TestData.ObjectType("Foo") with { IsSealed = true };

        var sig = DocfxObjectSignature.Synthesise(type);

        await Assert.That(sig).IsEqualTo("public sealed class Foo");
    }

    /// <summary>Abstract class renders <c>public abstract class</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AbstractClassUsesAbstractModifier()
    {
        var type = TestData.ObjectType("Foo") with { IsAbstract = true };

        var sig = DocfxObjectSignature.Synthesise(type);

        await Assert.That(sig).IsEqualTo("public abstract class Foo");
    }

    /// <summary>Each <see cref="ApiObjectKind"/> maps to the right C# keyword.</summary>
    /// <param name="kind">Kind value.</param>
    /// <param name="expected">Expected keyword span.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(ApiObjectKind.Class, "class")]
    [Arguments(ApiObjectKind.Struct, "struct")]
    [Arguments(ApiObjectKind.Interface, "interface")]
    [Arguments(ApiObjectKind.Record, "record")]
    [Arguments(ApiObjectKind.RecordStruct, "record struct")]
    public async Task KindKeywordCoversEveryKind(ApiObjectKind kind, string expected)
    {
        var keyword = DocfxObjectSignature.KindKeyword(kind);

        await Assert.That(keyword).IsEqualTo(expected);
    }

    /// <summary>Class with a base type renders <c>: BaseName</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithBaseRendersColonBase()
    {
        var type = TestData.ObjectType("Sub") with
        {
            BaseType = new ApiTypeReference("Base", "T:My.Base"),
        };

        var sig = DocfxObjectSignature.Synthesise(type);

        await Assert.That(sig).IsEqualTo("public class Sub : Base");
    }

    /// <summary>Class with interfaces renders the comma-separated list after the base.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithBaseAndInterfacesRendersBoth()
    {
        var type = TestData.ObjectType("Foo") with
        {
            BaseType = new ApiTypeReference("Base", "T:My.Base"),
            Interfaces =
            [
                new ApiTypeReference("IBaz", "T:My.IBaz"),
                new ApiTypeReference("IQux", "T:My.IQux"),
            ],
        };

        var sig = DocfxObjectSignature.Synthesise(type);

        await Assert.That(sig).IsEqualTo("public class Foo : Base, IBaz, IQux");
    }

    /// <summary>Implicit System.Object base is suppressed (would clutter the signature).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ImplicitObjectBaseIsSuppressed()
    {
        var type = TestData.ObjectType("Foo") with
        {
            BaseType = new ApiTypeReference("Object", "T:System.Object"),
        };

        var sig = DocfxObjectSignature.Synthesise(type);

        await Assert.That(sig).IsEqualTo("public class Foo");
    }

    /// <summary>End-to-end: a class type's rendered YAML page now carries the new <c>syntax:</c> block.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypePageEmitsSyntaxBlockForClass()
    {
        var type = TestData.ObjectType("Foo") with
        {
            Interfaces = [new ApiTypeReference("IFoo", "T:My.IFoo")],
        };

        var yaml = DocfxYamlEmitter.Render(type);

        await Assert.That(yaml).Contains("syntax:");

        // The YAML scalar quoter wraps the value because the `:` after
        // the type name is a reserved indicator. Either quoted or
        // folded-block form is valid; the test only pins the substance.
        await Assert.That(yaml).Contains("public class Foo : IFoo");
    }
}
