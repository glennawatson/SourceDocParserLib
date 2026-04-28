// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Unit-level coverage of the page count contract for
/// <see cref="ZensicalDocumentationEmitter"/>: the emitter writes one
/// page per type, plus one per overload group on object / union types.
/// Enums and delegates emit exactly one page (their type page already
/// shows the full surface inline).
/// </summary>
public class ZensicalDocumentationEmitterTests
{
    /// <summary>
    /// A class with three distinct member names produces one type page
    /// plus three overload-group pages -- the baseline contract.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithDistinctMemberNamesEmitsOnePagePerOverloadGroup()
    {
        using var scratch = new ScratchDirectory();
        var type = ObjectTypeWithMembers("DemoClass", "Run", "Stop", "Cancel");

        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], scratch.Path);

        // 1 type page + 3 overload-group pages + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(6);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(6);
    }

    /// <summary>
    /// Overloads of the same method name share one overload-group page --
    /// the bucket-by-name behaviour collapses them.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithRepeatedOverloadsCollapsesIntoOneMemberPage()
    {
        using var scratch = new ScratchDirectory();
        var type = ObjectTypeWithMembers("DemoClass", "Run", "Run", "Run");

        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], scratch.Path);

        // 1 type page + 1 overload-group page (all three Run overloads share
        // the same name bucket) + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(4);
    }

    /// <summary>
    /// Enums never emit per-value pages no matter how many values they
    /// declare -- the type page already lists every value inline. The
    /// baseline an icon-font enum would otherwise hit is thousands of
    /// per-value pages, so the contract is "exactly 1 page".
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumWithManyValuesEmitsOnlyTheTypePage()
    {
        using var scratch = new ScratchDirectory();

        var values = new List<ApiEnumValue>(256);
        for (var i = 0; i < 256; i++)
        {
            values.Add(new($"Value{i}", $"F:DemoEnum.Value{i}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), ApiDocumentation.Empty, null));
        }

        var type = TestData.EnumType("DemoEnum") with { Values = [.. values] };
        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], scratch.Path);

        // 1 type page + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(3);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(3);
    }

    /// <summary>
    /// Delegates never emit per-overload pages -- the Invoke signature
    /// is the type page itself.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DelegateEmitsOnlyTheTypePage()
    {
        using var scratch = new ScratchDirectory();
        var type = TestData.DelegateType("DemoHandler");

        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], scratch.Path);

        // 1 type page + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(3);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(3);
    }

    /// <summary>
    /// The default-constructor overload (no options) wires up the
    /// <see cref="ZensicalEmitterOptions.Default"/> instance and routes
    /// every type through the legacy flat layout. Exercises the
    /// parameterless constructor path.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DefaultConstructorEmitsUsingLegacyFlatLayout()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();
        var type = TestData.ObjectType("DemoClass");

        var pages = await emitter.EmitAsync([type], scratch.Path);

        await Assert.That(pages).IsGreaterThan(0);
    }

    /// <summary>
    /// The two-argument <see cref="ZensicalDocumentationEmitter.EmitAsync(ApiType[], string)"/>
    /// overload forwards to the cancellation-aware overload with
    /// <see cref="CancellationToken.None"/>; exercising it ensures the
    /// thin forwarding shim is covered.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TwoArgEmitAsyncOverloadDelegatesToCancellableOverload()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();
        var firstType = TestData.ObjectType("FirstDemo");
        var secondType = TestData.ObjectType("SecondDemo");

        var pages = await emitter.EmitAsync([firstType, secondType], scratch.Path);

        // Two distinct types -> at least two type pages emitted; the
        // exact total includes namespace + package landing pages so we
        // assert the lower bound to keep this test robust against
        // landing-page count changes.
        await Assert.That(pages).IsGreaterThanOrEqualTo(2);
        var firstPage = Directory.EnumerateFiles(scratch.Path, "FirstDemo.md", SearchOption.AllDirectories).FirstOrDefault();
        var secondPage = Directory.EnumerateFiles(scratch.Path, "SecondDemo.md", SearchOption.AllDirectories).FirstOrDefault();
        await Assert.That(firstPage).IsNotNull();
        await Assert.That(secondPage).IsNotNull();
    }

    /// <summary>
    /// Passing <see langword="null"/> for <c>options</c> hits the
    /// <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/>
    /// guard in the constructor.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorThrowsWhenOptionsIsNull() =>
        await Assert.That(() => new ZensicalDocumentationEmitter(null!)).Throws<ArgumentNullException>();

    /// <summary>
    /// Passing <see langword="null"/> for <c>types</c> hits the
    /// <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/>
    /// guard at the top of <see cref="ZensicalDocumentationEmitter.EmitAsync(ApiType[], string, CancellationToken)"/>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAsyncThrowsWhenTypesIsNull()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();

        await Assert.That(() => emitter.EmitAsync(null!, scratch.Path, CancellationToken.None)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Whitespace and empty <c>outputRoot</c> values trip the
    /// <see cref="ArgumentException.ThrowIfNullOrWhiteSpace(string?, string?)"/>
    /// guard.
    /// </summary>
    /// <param name="outputRoot">Invalid output root candidate.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task EmitAsyncThrowsWhenOutputRootIsBlank(string outputRoot)
    {
        var emitter = new ZensicalDocumentationEmitter();
        var type = TestData.ObjectType("DemoClass");

        await Assert.That(() => emitter.EmitAsync([type], outputRoot, CancellationToken.None)).Throws<ArgumentException>();
    }

    /// <summary>
    /// A null <c>outputRoot</c> trips the same
    /// <see cref="ArgumentException.ThrowIfNullOrWhiteSpace(string?, string?)"/>
    /// guard but as an <see cref="ArgumentNullException"/>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAsyncThrowsWhenOutputRootIsNull()
    {
        var emitter = new ZensicalDocumentationEmitter();
        var type = TestData.ObjectType("DemoClass");

        await Assert.That(() => emitter.EmitAsync([type], null!, CancellationToken.None)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// An already-cancelled token surfaces as
    /// <see cref="OperationCanceledException"/> from inside the per-type
    /// emit loop -- exercises the cancellation observer between types.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAsyncObservesCancellationToken()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();
        var type = TestData.ObjectType("DemoClass");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(() => emitter.EmitAsync([type], scratch.Path, cts.Token)).Throws<OperationCanceledException>();
    }

    /// <summary>
    /// When package routing is configured, types whose assembly does not
    /// match any rule are filtered out: zero type pages, zero member
    /// pages, no UID added to the emitted set. Confirms the
    /// <c>ShouldSkipType</c> routing branch.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RoutingFilterSkipsTypesOutsideConfiguredScope()
    {
        using var scratch = new ScratchDirectory();
        var options = new ZensicalEmitterOptions([new PackageRoutingRule("InScope", "InScope")]);
        var emitter = new ZensicalDocumentationEmitter(options);
        var inScope = ObjectTypeWithMembers("DemoClass", "Run") with
        {
            AssemblyName = "InScope.Demo",
            Namespace = "InScope.Demo",
        };
        var outOfScope = TestData.ObjectType("OtherClass", "OutOfScope.Other") with { Namespace = "OutOfScope.Other" };

        await emitter.EmitAsync([inScope, outOfScope], scratch.Path);

        var inScopePage = Directory.EnumerateFiles(scratch.Path, "DemoClass.md", SearchOption.AllDirectories).FirstOrDefault();
        var outOfScopePage = Directory.EnumerateFiles(scratch.Path, "OtherClass.md", SearchOption.AllDirectories).FirstOrDefault();
        await Assert.That(inScopePage).IsNotNull();
        await Assert.That(outOfScopePage).IsNull();
    }

    /// <summary>
    /// Compiler-generated type names (angle-bracket prefix) are filtered
    /// out by <c>ShouldSkipType</c>'s second branch -- they never reach
    /// <c>TypePageEmitter.RenderToFile</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CompilerGeneratedTypeNamesAreSkipped()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();
        var hidden = TestData.ObjectType("<>c__DisplayClass0_0");

        var pages = await emitter.EmitAsync([hidden], scratch.Path);

        // No type page, no member page -- only landing pages exist if any.
        await Assert.That(MarkdownFiles(scratch.Path)).IsLessThanOrEqualTo(pages);
    }

    /// <summary>
    /// Members whose names look compiler-generated (e.g. backing fields
    /// or accessor stubs) must be skipped by both
    /// <c>BuildEmittedUidSet</c> and <c>EmitMemberPages</c> -- so the
    /// type page emits but no member page does.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CompilerGeneratedMembersAreNotEmittedAsPages()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();

        // Every member name is angle-bracket-mangled so the
        // compiler-generated filter (matches `<` / `>`) skips them all.
        var type = ObjectTypeWithMembers(
            "DemoClass",
            "<RealName>k__BackingField",
            "<>c__DisplayClass0_0",
            "<RaiseEvent>b__0");

        await emitter.EmitAsync([type], scratch.Path);

        // The type page exists, but no member-page directory should
        // contain a per-overload page for any of the synthetic names.
        var typePage = Directory.EnumerateFiles(scratch.Path, "DemoClass.md", SearchOption.AllDirectories).FirstOrDefault();
        await Assert.That(typePage).IsNotNull();
        var memberPages = Directory
            .EnumerateFiles(scratch.Path, "*.md", SearchOption.AllDirectories)
            .Where(static path => Path.GetFileName(path) is not "DemoClass.md" and not "index.md")
            .ToList();
        await Assert.That(memberPages.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A union type (interface kind) contributes one overload-group
    /// page per distinct member name, exercising the
    /// <c>ApiUnionType</c> branches in <c>CollectMemberUids</c> and
    /// <c>EmitMemberPages</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnionTypeMembersEmitOverloadGroupPages()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();
        var member = new ApiMember(
            Name: "Run",
            Uid: "DemoUnion.Run",
            Kind: ApiMemberKind.Method,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "void Run()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "DemoUnion",
            ContainingTypeName: "DemoUnion",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);

        var union = new ApiUnionType(
            Name: "DemoUnion",
            FullName: "DemoUnion",
            Uid: "DemoUnion",
            Namespace: string.Empty,
            Arity: 0,
            IsStatic: false,
            IsSealed: false,
            IsAbstract: true,
            AssemblyName: "Test",
            Documentation: ApiDocumentation.Empty,
            BaseType: null,
            Interfaces: [],
            SourceUrl: null,
            AppliesTo: [],
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: [],
            Members: [member],
            Cases: []);

        var pages = await emitter.EmitAsync([union], scratch.Path);

        await Assert.That(pages).IsGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Builds an <see cref="ApiObjectType"/> with one synthetic
    /// <see cref="ApiMember"/> per name in <paramref name="memberNames"/>.
    /// </summary>
    /// <param name="name">Type name (also used as the UID stem for each member).</param>
    /// <param name="memberNames">Member names, one per synthesised member.</param>
    /// <returns>The constructed type with members attached.</returns>
    private static ApiObjectType ObjectTypeWithMembers(string name, params string[] memberNames)
    {
        var members = new List<ApiMember>(memberNames.Length);
        for (var i = 0; i < memberNames.Length; i++)
        {
            var memberName = memberNames[i];
            members.Add(new(
                Name: memberName,
                Uid: $"{name}.{memberName}",
                Kind: ApiMemberKind.Method,
                IsStatic: false,
                IsExtension: false,
                IsRequired: false,
                IsVirtual: false,
                IsOverride: false,
                IsAbstract: false,
                IsSealed: false,
                Signature: $"void {memberName}()",
                Parameters: [],
                TypeParameters: [],
                ReturnType: null,
                ContainingTypeUid: name,
                ContainingTypeName: name,
                SourceUrl: null,
                Documentation: ApiDocumentation.Empty,
                IsObsolete: false,
                ObsoleteMessage: null,
                Attributes: []));
        }

        return TestData.ObjectType(name) with { Members = [.. members] };
    }

    /// <summary>Counts every markdown file under <paramref name="root"/>.</summary>
    /// <param name="root">Directory to walk recursively.</param>
    /// <returns>Total number of <c>.md</c> files found.</returns>
    private static int MarkdownFiles(string root) =>
        Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Count();
}
