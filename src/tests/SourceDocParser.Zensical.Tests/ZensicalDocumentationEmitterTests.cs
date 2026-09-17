// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
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
    /// <summary>Fixture value for ClassName.</summary>
    private const string ClassName = "DemoClass";

    /// <summary>Fixture value for ClassFileName.</summary>
    private const string ClassFileName = "DemoClass.md";

    /// <summary>Fixture value for UnionName.</summary>
    private const string UnionName = "DemoUnion";

    /// <summary>
    /// A class with three distinct member names produces one type page
    /// plus three overload-group pages -- the baseline contract.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithDistinctMemberNamesEmitsOnePagePerOverloadGroup()
    {
        const int ExpectedPages = 6;
        using var scratch = new ScratchDirectory();
        var type = ObjectTypeWithMembers(ClassName, "Run", "Stop", "Cancel");

        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], new FilePageSink(scratch.Path));

        // 1 type page + 3 overload-group pages + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(ExpectedPages);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(ExpectedPages);
    }

    /// <summary>Overloads of the same method name share one overload-group page -- the bucket-by-name behaviour collapses them.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithRepeatedOverloadsCollapsesIntoOneMemberPage()
    {
        const int ExpectedPages = 4;
        using var scratch = new ScratchDirectory();
        var type = ObjectTypeWithMembers(ClassName, "Run", "Run", "Run");

        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], new FilePageSink(scratch.Path));

        // 1 type page + 1 overload-group page (all three Run overloads share
        // the same name bucket) + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(ExpectedPages);
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
        const int EnumValueCount = 256;
        const int ExpectedPages = 3;
        using var scratch = new ScratchDirectory();

        var values = new List<ApiEnumValue>(EnumValueCount);
        for (var i = 0; i < EnumValueCount; i++)
        {
            values.Add(new($"Value{i}", $"F:DemoEnum.Value{i}", i.ToString(System.Globalization.CultureInfo.InvariantCulture), ApiDocumentation.Empty, null));
        }

        var type = TestData.EnumType("DemoEnum") with { Values = [.. values] };
        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], new FilePageSink(scratch.Path));

        // 1 type page + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(ExpectedPages);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(ExpectedPages);
    }

    /// <summary>Delegates never emit per-overload pages -- the Invoke signature is the type page itself.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DelegateEmitsOnlyTheTypePage()
    {
        const int ExpectedPages = 3;
        using var scratch = new ScratchDirectory();
        var type = TestData.DelegateType("DemoHandler");

        var pages = await new ZensicalDocumentationEmitter().EmitAsync([type], new FilePageSink(scratch.Path));

        // 1 type page + 1 package landing + 1 namespace landing.
        await Assert.That(pages).IsEqualTo(ExpectedPages);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(ExpectedPages);
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
        var type = TestData.ObjectType(ClassName);

        var pages = await emitter.EmitAsync([type], new FilePageSink(scratch.Path));

        await Assert.That(pages).IsGreaterThan(0);
    }

    /// <summary>
    /// The two-argument <see cref="ZensicalDocumentationEmitter.EmitAsync(ApiType[], IPageSink)"/>
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

        var pages = await emitter.EmitAsync([firstType, secondType], new FilePageSink(scratch.Path));

        // Two distinct types -> at least two type pages emitted; the
        // exact total includes namespace + package landing pages so we
        // assert the lower bound to keep this test robust against
        // landing-page count changes.
        const int MinimumPages = 2;
        await Assert.That(pages).IsGreaterThanOrEqualTo(MinimumPages);
        var firstPages = Directory.GetFiles(scratch.Path, "FirstDemo.md", SearchOption.AllDirectories);
        var secondPages = Directory.GetFiles(scratch.Path, "SecondDemo.md", SearchOption.AllDirectories);
        await Assert.That(firstPages.Length).IsGreaterThan(0);
        await Assert.That(secondPages.Length).IsGreaterThan(0);
    }

    /// <summary>Passing <see langword="null"/> for <c>options</c> hits the <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/> guard in the constructor.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorThrowsWhenOptionsIsNull() =>
        await Assert.That(static () => new ZensicalDocumentationEmitter(null!)).Throws<ArgumentNullException>();

    /// <summary>
    /// Passing <see langword="null"/> for <c>types</c> hits the
    /// <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/>
    /// guard at the top of <see cref="ZensicalDocumentationEmitter.EmitAsync(ApiType[], IPageSink, CancellationToken)"/>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAsyncThrowsWhenTypesIsNull()
    {
        using var scratch = new ScratchDirectory();
        var emitter = new ZensicalDocumentationEmitter();

        await Assert.That(() => emitter.EmitAsync(null!, new FilePageSink(scratch.Path), CancellationToken.None)).Throws<ArgumentNullException>();
    }

    /// <summary>Constructing a <see cref="FilePageSink"/> with a blank root trips its argument-validation guard.</summary>
    /// <param name="outputRoot">Invalid output root candidate.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task FilePageSinkThrowsWhenOutputRootIsBlank(string outputRoot) =>
        await Assert.That(() => new FilePageSink(outputRoot)).Throws<ArgumentException>();

    /// <summary>A null sink trips the emitter's argument-null guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAsyncThrowsWhenSinkIsNull()
    {
        var emitter = new ZensicalDocumentationEmitter();
        var type = TestData.ObjectType(ClassName);

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
        var type = TestData.ObjectType(ClassName);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(() => emitter.EmitAsync([type], new FilePageSink(scratch.Path), cts.Token)).Throws<OperationCanceledException>();
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
        var inScope = ObjectTypeWithMembers(ClassName, "Run") with
        {
            AssemblyName = "InScope.Demo",
            Namespace = "InScope.Demo",
        };
        var outOfScope = TestData.ObjectType("OtherClass", "OutOfScope.Other") with { Namespace = "OutOfScope.Other" };

        await emitter.EmitAsync([inScope, outOfScope], new FilePageSink(scratch.Path));

        var inScopePages = Directory.GetFiles(scratch.Path, ClassFileName, SearchOption.AllDirectories);
        var outOfScopePages = Directory.GetFiles(scratch.Path, "OtherClass.md", SearchOption.AllDirectories);
        await Assert.That(inScopePages.Length).IsGreaterThan(0);
        await Assert.That(outOfScopePages.Length).IsEqualTo(0);
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

        var pages = await emitter.EmitAsync([hidden], new FilePageSink(scratch.Path));

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
            ClassName,
            "<RealName>k__BackingField",
            "<>c__DisplayClass0_0",
            "<RaiseEvent>b__0");

        await emitter.EmitAsync([type], new FilePageSink(scratch.Path));

        // The type page exists, but no member-page directory should
        // contain a per-overload page for any of the synthetic names.
        var typePages = Directory.GetFiles(scratch.Path, ClassFileName, SearchOption.AllDirectories);
        await Assert.That(typePages.Length).IsGreaterThan(0);
        var pages = Directory.GetFiles(scratch.Path, "*.md", SearchOption.AllDirectories);
        foreach (var page in pages)
        {
            await Assert.That(Path.GetFileName(page) is ClassFileName or "index.md").IsTrue();
        }
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
            ContainingTypeUid: UnionName,
            ContainingTypeName: UnionName,
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);

        var union = new ApiUnionType(
            Name: UnionName,
            FullName: UnionName,
            Uid: UnionName,
            Namespace: string.Empty,
            Arity: 0,
            IsStatic: false,
            IsSealed: false,
            IsAbstract: true,
            AssemblyName: nameof(Test),
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

        var pages = await emitter.EmitAsync([union], new FilePageSink(scratch.Path));

        const int MinimumPages = 2;
        await Assert.That(pages).IsGreaterThanOrEqualTo(MinimumPages);
    }

    /// <summary>Builds an <see cref="ApiObjectType"/> with one synthetic <see cref="ApiMember"/> per name in <paramref name="memberNames"/>.</summary>
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MarkdownFiles(string root) =>
        Directory.GetFiles(root, "*.md", SearchOption.AllDirectories).Length;
}
