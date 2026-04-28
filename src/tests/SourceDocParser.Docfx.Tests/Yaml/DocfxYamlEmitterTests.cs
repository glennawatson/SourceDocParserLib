// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using YamlDotNet.RepresentationModel;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the DocfxYamlEmitter contract -- header / per-kind item shape /
/// references list / commentId mapping -- by parsing the generated YAML
/// back through YamlDotNet's RepresentationModel and asserting on the
/// resulting node tree. Round-tripping rather than string-matching
/// keeps the tests robust against whitespace tweaks while still
/// catching schema regressions.
/// </summary>
public class DocfxYamlEmitterTests
{
    /// <summary>
    /// Builds the parameterised arguments for
    /// <see cref="TypeItemCarriesRequiredDocfxFields"/>.
    /// </summary>
    /// <returns>One <c>(label, type)</c> pair per derivation.</returns>
    public static IEnumerable<Func<(string KindLabel, ApiType Type)>> EveryKindOfType()
    {
        yield return static () => ("Class", TestData.ObjectType("Foo"));
        yield return static () => ("Struct", TestData.ObjectType("Bar", ApiObjectKind.Struct));
        yield return static () => ("Interface", TestData.ObjectType("IFoo", ApiObjectKind.Interface));
        yield return static () => ("Record", TestData.ObjectType("Rec", ApiObjectKind.Record));
        yield return static () => ("RecordStruct", TestData.ObjectType("RecStr", ApiObjectKind.RecordStruct));
        yield return static () => ("Enum", TestData.EnumType("Day"));
        yield return static () => ("Delegate", TestData.DelegateType("Handler"));
    }

    /// <summary>
    /// Builds the parameterised arguments for
    /// <see cref="TypeFieldUsesDocfxKindLabel"/>.
    /// </summary>
    /// <returns>One <c>(type, expectedLabel)</c> pair per derivation.</returns>
    public static IEnumerable<Func<(ApiType Type, string Expected)>> KindToDocfxLabel()
    {
        yield return static () => (TestData.ObjectType("Foo"), "Class");
        yield return static () => (TestData.ObjectType("Bar", ApiObjectKind.Struct), "Struct");
        yield return static () => (TestData.ObjectType("IFoo", ApiObjectKind.Interface), "Interface");
        yield return static () => (TestData.ObjectType("Rec", ApiObjectKind.Record), "Class");
        yield return static () => (TestData.ObjectType("RecStr", ApiObjectKind.RecordStruct), "Struct");
        yield return static () => (TestData.EnumType("Day"), "Enum");
        yield return static () => (TestData.DelegateType("Handler"), "Delegate");
    }

    /// <summary>
    /// Every page starts with the docfx YamlMime header and an items
    /// sequence with at least one entry -- the contract every consumer
    /// (docfx itself, downstream tooling) relies on for discovery.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsManagedReferenceHeader()
    {
        var type = TestData.ObjectType("Foo");
        var yaml = DocfxYamlEmitter.Render(type).Lf();

        await Assert.That(yaml).StartsWith(DocfxYamlEmitter.YamlMimeHeader);
        var root = ParseFirstDocument(yaml);
        await Assert.That(root.Children).ContainsKey(new YamlScalarNode("items"));
    }

    /// <summary>
    /// Object-type pages emit one type item plus one item per documented
    /// member; the type item carries a children list whose entries match
    /// the member UIDs. Smoke-tests the path that powers most of the
    /// generated tree.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsTypeAndMemberItemsForClass()
    {
        var member = NewMember("Run", "M:Foo.Run");
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var items = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")];

        await Assert.That(items.Children).Count().IsEqualTo(2);
        var typeItem = (YamlMappingNode)items.Children[0];
        var memberItem = (YamlMappingNode)items.Children[1];

        await Assert.That(typeItem[new YamlScalarNode("uid")].ToString()).IsEqualTo("Foo");
        await Assert.That(typeItem[new YamlScalarNode("type")].ToString()).IsEqualTo("Class");
        await Assert.That(memberItem[new YamlScalarNode("uid")].ToString()).IsEqualTo("Foo.Run");
        await Assert.That(memberItem[new YamlScalarNode("parent")].ToString()).IsEqualTo("Foo");
        await Assert.That(memberItem[new YamlScalarNode("type")].ToString()).IsEqualTo("Method");

        var children = (YamlSequenceNode)typeItem[new YamlScalarNode("children")];
        await Assert.That(children.Children).Count().IsEqualTo(1);
        await Assert.That(children.Children[0].ToString()).IsEqualTo("Foo.Run");
    }

    /// <summary>
    /// Enum pages skip per-value member items but surface the values
    /// under a syntax -> parameters list -- the docfx convention that
    /// lets the default template render them as a value table.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsEnumValuesUnderSyntaxParameters()
    {
        var values = new List<ApiEnumValue>
        {
            new("Friday", "F:Day.Friday", "5", ApiDocumentation.Empty, null),
            new("Saturday", "F:Day.Saturday", "6", ApiDocumentation.Empty, null),
        };
        var type = TestData.EnumType("Day") with { Values = [.. values] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var items = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")];

        // No per-value member items -- a single type item carries the values inline.
        await Assert.That(items.Children).Count().IsEqualTo(1);
        var typeItem = (YamlMappingNode)items.Children[0];
        await Assert.That(typeItem[new YamlScalarNode("type")].ToString()).IsEqualTo("Enum");

        var syntax = (YamlMappingNode)typeItem[new YamlScalarNode("syntax")];
        var parameters = (YamlSequenceNode)syntax[new YamlScalarNode("parameters")];
        await Assert.That(parameters.Children).Count().IsEqualTo(2);
        var first = (YamlMappingNode)parameters.Children[0];
        await Assert.That(first[new YamlScalarNode("id")].ToString()).IsEqualTo("Friday");
        await Assert.That(first[new YamlScalarNode("defaultValue")].ToString()).IsEqualTo("5");
    }

    /// <summary>
    /// Delegate pages emit only the type item and surface the Invoke
    /// signature under syntax -> content. No per-overload member items
    /// -- the page itself is the signature.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsDelegateSignatureUnderSyntax()
    {
        var type = TestData.DelegateType("Handler");

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var items = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")];

        await Assert.That(items.Children).Count().IsEqualTo(1);
        var typeItem = (YamlMappingNode)items.Children[0];
        await Assert.That(typeItem[new YamlScalarNode("type")].ToString()).IsEqualTo("Delegate");
        var syntax = (YamlMappingNode)typeItem[new YamlScalarNode("syntax")];
        await Assert.That(syntax[new YamlScalarNode("content")].ToString()).Contains("Handler()");
    }

    /// <summary>
    /// Scalar values that look like YAML booleans / nulls / leading-dash
    /// strings get quoted so the parser doesn't reinterpret them. Round-
    /// trip the value through YamlDotNet and assert the original string
    /// comes back intact.
    /// </summary>
    /// <param name="raw">Raw scalar value to round-trip.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("null")]
    [Arguments("true")]
    [Arguments("- leading dash")]
    [Arguments(": leading colon")]
    [Arguments("multi\nline")]
    public async Task ScalarsRoundTripThroughYamlDotNet(string raw)
    {
        // Embed the raw scalar as a member name so the page contains it
        // both in items[].name and items[].uid; whatever escape strategy
        // the emitter picks must survive a round-trip parse. Use a
        // property-kind member so the friendly-name pass doesn't append
        // parens -- this test is about YAML scalar correctness, not name
        // policy.
        var member = NewMember(raw, $"M:Foo.{raw}", ApiMemberKind.Property);
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var items = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")];
        var memberItem = (YamlMappingNode)items.Children[1];

        await Assert.That(memberItem[new YamlScalarNode("name")].ToString()).IsEqualTo(raw);
    }

    /// <summary>
    /// Each ApiTypeReference the page mentions (base type, interfaces,
    /// member parameter / return types) lands under references[] with
    /// uid + commentId. Verifies dedup so duplicates don't slip in.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderCollectsDistinctReferencesIntoPageReferences()
    {
        var stringRef = new ApiTypeReference("String", "T:System.String");
        var intRef = new ApiTypeReference("Int32", "T:System.Int32");
        var member = NewMember("Run", "M:Foo.Run") with
        {
            ReturnType = stringRef,
            Parameters =
            [
                new("arg", stringRef, false, false, false, false, false, null),
                new("count", intRef, false, false, false, false, false, null),
            ],
        };
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var refs = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("references")];

        // String shows up twice (return + parameter) but should be
        // deduplicated; Int32 shows up once. System.Object is added by
        // the well-known-base synthesis for class types, bringing the
        // expected count to 3.
        await Assert.That(refs.Children).Count().IsEqualTo(3);
        var refUids = refs.Children
            .Cast<YamlMappingNode>()
            .Select(static n => n[new YamlScalarNode("uid")].ToString())
            .ToList();
        await Assert.That(refUids).Contains("System.String");
        await Assert.That(refUids).Contains("System.Int32");
        await Assert.That(refUids).Contains("System.Object");
    }

    /// <summary>
    /// PathFor mirrors docfx's UID-stem-with-extension convention so
    /// the emitted file lands where docfx's metadata extractor would
    /// have put it. Generic angle brackets get sanitised to underscores
    /// to keep the stem filesystem-safe.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForUsesUidStemWithExtension()
    {
        var simple = TestData.ObjectType("Foo.Bar");
        var generic = TestData.ObjectType("Foo.Bar<T>");
        var unsafeUid = TestData.ObjectType("Foo") with { Uid = "T:Foo/Bar<Baz>\"Qux\"" };

        await Assert.That(DocfxYamlEmitter.PathFor(simple)).IsEqualTo("Foo.Bar.yml");
        await Assert.That(DocfxYamlEmitter.PathFor(generic)).IsEqualTo("Foo.Bar_T_.yml");
        await Assert.That(DocfxYamlEmitter.PathFor(unsafeUid)).IsEqualTo("Foo_Bar_Baz__Qux_.yml");
    }

    /// <summary>
    /// AppendQualifiedScalar fast-paths quote-safe identifiers to two
    /// raw appends + a separator, skipping the per-call allocation of
    /// the joined string. Verifies the output round-trips.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QualifiedNameWithSafeIdentifiersStaysUnquoted()
    {
        // Use property kind so the friendly-name pass doesn't append
        // parens -- this test pins the qualified-scalar quoting policy,
        // not the method-naming convention.
        var member = NewMember("Run", "P:Foo.Run", ApiMemberKind.Property);
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var memberItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[1];

        await Assert.That(memberItem[new YamlScalarNode("nameWithType")].ToString()).IsEqualTo("Foo.Run");

        // The value should be written unquoted (the dotted form is a
        // valid YAML plain scalar; only the quoted-form path allocates).
        await Assert.That(yaml).Contains("\n  nameWithType: Foo.Run\n");
    }

    /// <summary>
    /// AppendQualifiedScalar falls back to a quoted scalar when either
    /// half contains characters that would be misparsed in plain form.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QualifiedNameQuotesWhenEitherHalfNeedsEscaping()
    {
        var member = NewMember(": leading colon", "P:Foo.member", ApiMemberKind.Property);
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var memberItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[1];

        // Round-trips back to the original composite -- the quoted form
        // preserves the leading colon docfx readers expect.
        await Assert.That(memberItem[new YamlScalarNode("nameWithType")].ToString()).IsEqualTo("Foo.: leading colon");
    }

    /// <summary>EmitAsync writes one .yml per type and returns the page count.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAsyncWritesOnePagePerType()
    {
        using var scratch = new ScratchDirectory("sdp-docfx-yaml");
        var types = new List<ApiType>
        {
            TestData.ObjectType("Foo"),
            TestData.EnumType("Day"),
            TestData.DelegateType("Handler"),
        };

        var pages = await new DocfxYamlEmitter().EmitAsync([.. types], scratch.Path);

        await Assert.That(pages).IsEqualTo(3);
        await Assert.That(File.Exists(Path.Combine(scratch.Path, "Foo.yml"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(scratch.Path, "Day.yml"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(scratch.Path, "Handler.yml"))).IsTrue();
    }

    /// <summary>
    /// Every type-item, regardless of derivation, carries the docfx-
    /// required field set: uid / id / langs (sequence) / name / type /
    /// assemblies (sequence). Verifies the contract on each derivation.
    /// </summary>
    /// <param name="kindLabel">Human-readable label for the failure message.</param>
    /// <param name="type">Type to render.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [MethodDataSource(nameof(EveryKindOfType))]
    public async Task TypeItemCarriesRequiredDocfxFields(string kindLabel, ApiType type)
    {
        _ = kindLabel;
        var typeItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(DocfxYamlEmitter.Render(type))
            .Children[new YamlScalarNode("items")]).Children[0];

        await Assert.That(typeItem.Children).ContainsKey(new YamlScalarNode("uid"));
        await Assert.That(typeItem.Children).ContainsKey(new YamlScalarNode("id"));
        await Assert.That(typeItem.Children).ContainsKey(new YamlScalarNode("langs"));
        await Assert.That(typeItem.Children).ContainsKey(new YamlScalarNode("name"));
        await Assert.That(typeItem.Children).ContainsKey(new YamlScalarNode("type"));
        await Assert.That(typeItem.Children).ContainsKey(new YamlScalarNode("assemblies"));

        await Assert.That(typeItem[new YamlScalarNode("langs")]).IsTypeOf<YamlSequenceNode>();
        await Assert.That(typeItem[new YamlScalarNode("assemblies")]).IsTypeOf<YamlSequenceNode>();
    }

    /// <summary>
    /// Every kind label maps to the exact docfx <c>type:</c> token its
    /// metadata extractor would produce -- the rest of the docfx
    /// pipeline pattern-matches on these strings, so any drift breaks
    /// downstream rendering.
    /// </summary>
    /// <param name="type">Type to render.</param>
    /// <param name="expected">Expected docfx <c>type:</c> token.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [MethodDataSource(nameof(KindToDocfxLabel))]
    public async Task TypeFieldUsesDocfxKindLabel(ApiType type, string expected)
    {
        var typeItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(DocfxYamlEmitter.Render(type))
            .Children[new YamlScalarNode("items")]).Children[0];

        await Assert.That(typeItem[new YamlScalarNode("type")].ToString()).IsEqualTo(expected);
    }

    /// <summary>
    /// Multi-line summaries become YAML literal blocks (<c>|-</c>) so
    /// each source line lands on its own indented line in the output;
    /// round-tripping recovers the exact original text.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MultiLineSummaryEmitsAsLiteralBlock()
    {
        const string summary = "First line.\nSecond line.\nThird line with `code`.";
        var docs = ApiDocumentation.Empty with { Summary = summary };
        var type = TestData.ObjectType("Foo") with { Documentation = docs };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var typeItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[0];

        await Assert.That(yaml).Contains("summary: |-\n");
        await Assert.That(typeItem[new YamlScalarNode("summary")].ToString()).IsEqualTo(summary);
    }

    /// <summary>
    /// Members with no parameters omit the parameters block entirely so
    /// the generated YAML stays as terse as docfx's own output.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParameterlessMemberOmitsParametersBlock()
    {
        var member = NewMember("Run", "M:Foo.Run") with { Parameters = [] };
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var memberItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[1];
        var syntax = (YamlMappingNode)memberItem[new YamlScalarNode("syntax")];

        await Assert.That(syntax.Children).DoesNotContainKey(new YamlScalarNode("parameters"));
    }

    /// <summary>
    /// Parameter default values surface under <c>defaultValue:</c> when
    /// present; YAML-reserved tokens (<c>null</c> in particular) survive
    /// round-trip because the scalar emitter quotes them.
    /// </summary>
    /// <param name="rawDefault">Raw default-value literal to round-trip.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("null")]
    [Arguments("0")]
    [Arguments("\"abc\"")]
    [Arguments("true")]
    [Arguments("System.Reflection.BindingFlags.Default")]
    public async Task ParameterDefaultValuesRoundTrip(string rawDefault)
    {
        var parameter = new ApiParameter(
            "value",
            new("Object", "T:System.Object"),
            IsOptional: true,
            IsParams: false,
            IsIn: false,
            IsOut: false,
            IsRef: false,
            DefaultValue: rawDefault);
        var member = NewMember("Configure", "M:Foo.Configure") with { Parameters = [parameter] };
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var memberItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[1];
        var syntax = (YamlMappingNode)memberItem[new YamlScalarNode("syntax")];
        var parameters = (YamlSequenceNode)syntax[new YamlScalarNode("parameters")];
        var paramEntry = (YamlMappingNode)parameters.Children[0];

        await Assert.That(paramEntry[new YamlScalarNode("defaultValue")].ToString()).IsEqualTo(rawDefault);
    }

    /// <summary>
    /// Generic types encode arity into their UID/name ('`1', '`2', etc.)
    /// per docfx convention -- verified through round-trip.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GenericTypeRoundTripsArityAndName()
    {
        var type = TestData.ObjectType("List`1") with { Arity = 1 };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var typeItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[0];

        await Assert.That(typeItem[new YamlScalarNode("uid")].ToString()).IsEqualTo("List`1");
        await Assert.That(typeItem[new YamlScalarNode("name")].ToString()).IsEqualTo("List`1");
    }

    /// <summary>
    /// Global-namespace types omit the <c>namespace:</c> field entirely
    /// -- emitting an empty value would parse as null and confuse docfx.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GlobalNamespaceTypeOmitsNamespaceField()
    {
        var type = TestData.ObjectType("Foo");

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var typeItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[0];

        await Assert.That(typeItem.Children).DoesNotContainKey(new YamlScalarNode("namespace"));
    }

    /// <summary>
    /// Empty types (no members, no base, no interfaces) still render a
    /// well-formed page with no <c>children:</c> / <c>references:</c>
    /// section but a complete type-item header.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyTypeStillRendersValidPage()
    {
        var type = TestData.ObjectType("Foo");

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var root = ParseFirstDocument(yaml);
        var items = (YamlSequenceNode)root.Children[new YamlScalarNode("items")];
        var typeItem = (YamlMappingNode)items.Children[0];

        // The well-known-base synthesis adds an `inheritance: System.Object`
        // line and a corresponding `references:` entry even for types
        // with no walked base, so those two keys now exist.
        await Assert.That(items.Children).Count().IsEqualTo(1);
        await Assert.That(typeItem.Children).DoesNotContainKey(new YamlScalarNode("children"));
        await Assert.That(typeItem.Children).DoesNotContainKey(new YamlScalarNode("implements"));
        await Assert.That(typeItem.Children).ContainsKey(new YamlScalarNode("inheritance"));
        await Assert.That(root.Children).ContainsKey(new YamlScalarNode("references"));
    }

    /// <summary>
    /// Union types emit each case under <c>references:</c> so docfx's
    /// cross-link rendering can surface the full closed hierarchy.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnionTypeEmitsCasesAsReferences()
    {
        var caseRefs = new List<ApiTypeReference>
        {
            new("Circle", "T:My.Shape.Circle"),
            new("Square", "T:My.Shape.Square"),
        };
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
            Cases: [.. caseRefs]);

        var yaml = DocfxYamlEmitter.Render(union).Lf();
        var refs = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("references")];
        var refUids = refs.Children
            .Cast<YamlMappingNode>()
            .Select(static n => n[new YamlScalarNode("uid")].ToString())
            .ToList();

        await Assert.That(refUids).Contains("My.Shape.Circle");
        await Assert.That(refUids).Contains("My.Shape.Square");
    }

    /// <summary>
    /// Inheritance + implements blocks land under their docfx-expected
    /// keys, with each entry as a sequence item.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritanceAndInterfacesEmittedUnderExpectedKeys()
    {
        var type = TestData.ObjectType("Derived") with
        {
            BaseType = new("Base", "T:My.Base"),
            Interfaces =
            [
                new("IDisposable", "T:System.IDisposable"),
                new("IFoo", "T:My.IFoo"),
            ],
        };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var typeItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[0];

        var inheritance = (YamlSequenceNode)typeItem[new YamlScalarNode("inheritance")];
        var implements = (YamlSequenceNode)typeItem[new YamlScalarNode("implements")];

        await Assert.That(inheritance.Children.Single().ToString()).IsEqualTo("My.Base");
        await Assert.That(implements.Children).Count().IsEqualTo(2);
    }

    /// <summary>
    /// References that lack a UID still emit a synthesised
    /// <c>commentId</c> with a <c>T:</c> prefix so the docfx xrefmap
    /// build can still produce a stable key.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReferenceWithoutUidGetsSynthesisedTPrefix()
    {
        var type = TestData.ObjectType("Foo") with
        {
            BaseType = new("Bare", string.Empty),
        };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var refs = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("references")];
        var refEntry = (YamlMappingNode)refs.Children[0];

        await Assert.That(refEntry[new YamlScalarNode("uid")].ToString()).IsEqualTo("Bare");
        await Assert.That(refEntry[new YamlScalarNode("commentId")].ToString()).IsEqualTo("T:Bare");
    }

    /// <summary>
    /// Page is parseable as a single YAML document -- guards against
    /// stray document separators (<c>---</c>) or accidental multiple
    /// documents in the output stream.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderProducesExactlyOneYamlDocument()
    {
        var type = TestData.ObjectType("Foo") with { Members = [NewMember("Run", "M:Foo.Run")] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);

        await Assert.That(stream.Documents).Count().IsEqualTo(1);
    }

    /// <summary>
    /// Non-ASCII characters in member names round-trip through the
    /// scalar escape path -- UTF-8 emitters and YamlDotNet readers
    /// agree on the encoding.
    /// </summary>
    /// <param name="name">Name containing non-ASCII codepoints.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Über")]
    [Arguments("名前")]
    [Arguments("café")]
    [Arguments("emoji_🎉")]
    public async Task NonAsciiMemberNamesRoundTrip(string name)
    {
        // Property kind so the friendly-name pass doesn't append parens
        // -- this test pins UTF-8 round-trip via the YAML scalar writer.
        var member = NewMember(name, $"P:Foo.{name}", ApiMemberKind.Property);
        var type = TestData.ObjectType("Foo") with { Members = [member] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var memberItem = (YamlMappingNode)((YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")]).Children[1];

        await Assert.That(memberItem[new YamlScalarNode("name")].ToString()).IsEqualTo(name);
    }

    /// <summary>
    /// Pages with very large member counts (here 500) stay within the
    /// docfx schema and parse cleanly -- pins behaviour for control-
    /// heavy WPF / MAUI types whose dependency-property surface scales
    /// to the low hundreds.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LargeMemberCountStillProducesValidPage()
    {
        var members = new List<ApiMember>(500);
        for (var i = 0; i < 500; i++)
        {
            members.Add(NewMember($"Method{i:D3}", $"M:Foo.Method{i:D3}"));
        }

        var type = TestData.ObjectType("Foo") with { Members = [.. members] };

        var yaml = DocfxYamlEmitter.Render(type).Lf();
        var items = (YamlSequenceNode)ParseFirstDocument(yaml).Children[new YamlScalarNode("items")];

        await Assert.That(items.Children).Count().IsEqualTo(501);
        var typeItem = (YamlMappingNode)items.Children[0];
        var children = (YamlSequenceNode)typeItem[new YamlScalarNode("children")];
        await Assert.That(children.Children).Count().IsEqualTo(500);
    }

    /// <summary>
    /// PathFor is deterministic -- repeated calls with the same input
    /// produce the same output.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForIsStableAcrossRepeatedCalls()
    {
        var generic = TestData.ObjectType("Outer.List<T>");

        var first = DocfxYamlEmitter.PathFor(generic);
        var second = DocfxYamlEmitter.PathFor(generic);
        var third = DocfxYamlEmitter.PathFor(generic);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(second).IsEqualTo(third);
    }

    /// <summary>
    /// Parses <paramref name="yaml"/> with YamlDotNet and returns the
    /// root mapping node of the first (and only) document.
    /// </summary>
    /// <param name="yaml">YAML text to parse.</param>
    /// <returns>The root mapping node.</returns>
    private static YamlMappingNode ParseFirstDocument(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    /// <summary>Builds a minimal <see cref="ApiMember"/> with the given name and UID.</summary>
    /// <param name="name">Member name.</param>
    /// <param name="uid">Member UID (Roslyn-style).</param>
    /// <param name="kind">Member kind -- defaults to <see cref="ApiMemberKind.Method"/>.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember NewMember(string name, string uid, ApiMemberKind kind = ApiMemberKind.Method) => new(
        Name: name,
        Uid: uid,
        Kind: kind,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: $"void {name}()",
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
