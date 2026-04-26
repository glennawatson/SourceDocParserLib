// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Unit-level coverage of the page count contract for
/// <see cref="ZensicalDocumentationEmitter"/>: the emitter writes one
/// page per type, plus one per overload group on classes/structs/
/// interfaces, but never per-value pages on enums or per-overload pages
/// on delegates (their type page already shows the full surface inline).
/// </summary>
public class ZensicalDocumentationEmitterTests
{
    /// <summary>
    /// A class with three distinct member names produces one type page
    /// plus three overload-group pages — the baseline contract.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithDistinctMemberNamesEmitsOnePagePerOverloadGroup()
    {
        using var scratch = new ScratchDirectory();

        var type = TypeWithMembers(
            "DemoClass",
            ApiTypeKind.Class,
            "Run",
            "Stop",
            "Cancel");

        var emitter = new ZensicalDocumentationEmitter();
        var pages = await emitter.EmitAsync([type], scratch.Path);

        // 1 type page + 3 overload-group pages.
        await Assert.That(pages).IsEqualTo(4);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(4);
    }

    /// <summary>
    /// Overloads of the same method name share one overload-group page —
    /// the bucket-by-name behaviour collapses them.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassWithRepeatedOverloadsCollapsesIntoOneMemberPage()
    {
        using var scratch = new ScratchDirectory();

        var type = TypeWithMembers(
            "DemoClass",
            ApiTypeKind.Class,
            "Run",
            "Run",
            "Run");

        var emitter = new ZensicalDocumentationEmitter();
        var pages = await emitter.EmitAsync([type], scratch.Path);

        // 1 type page + 1 overload-group page (all three Run overloads
        // share the same name bucket).
        await Assert.That(pages).IsEqualTo(2);
    }

    /// <summary>
    /// Enums never emit per-member pages no matter how many values they
    /// declare — the type page already lists every value inline. The
    /// baseline an icon-font enum would otherwise hit is thousands of
    /// per-value pages, so the contract is "exactly 1 page".
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumWithManyValuesEmitsOnlyTheTypePage()
    {
        using var scratch = new ScratchDirectory();

        var memberNames = new string[256];
        for (var i = 0; i < memberNames.Length; i++)
        {
            memberNames[i] = $"Value{i}";
        }

        var type = TypeWithMembers("DemoEnum", ApiTypeKind.Enum, memberNames);

        var emitter = new ZensicalDocumentationEmitter();
        var pages = await emitter.EmitAsync([type], scratch.Path);

        await Assert.That(pages).IsEqualTo(1);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(1);
    }

    /// <summary>
    /// Delegates never emit per-member pages — the signature is the type
    /// page itself.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DelegateEmitsOnlyTheTypePage()
    {
        using var scratch = new ScratchDirectory();

        var type = TypeWithMembers(
            "DemoHandler",
            ApiTypeKind.Delegate,
            "Invoke",
            "BeginInvoke",
            "EndInvoke");

        var emitter = new ZensicalDocumentationEmitter();
        var pages = await emitter.EmitAsync([type], scratch.Path);

        await Assert.That(pages).IsEqualTo(1);
        await Assert.That(MarkdownFiles(scratch.Path)).IsEqualTo(1);
    }

    /// <summary>
    /// Builds an <see cref="ApiType"/> of the requested kind with one
    /// synthetic <see cref="ApiMember"/> per name in <paramref name="memberNames"/>.
    /// </summary>
    /// <param name="name">Type name (also used as the UID stem for each member).</param>
    /// <param name="kind">Type kind to assign.</param>
    /// <param name="memberNames">Member names, one per synthesised member.</param>
    /// <returns>The constructed type with members attached.</returns>
    private static ApiType TypeWithMembers(string name, ApiTypeKind kind, params string[] memberNames)
    {
        var members = new List<ApiMember>(memberNames.Length);
        foreach (var memberName in memberNames)
        {
            members.Add(new ApiMember(
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
                Documentation: ApiDocumentation.Empty));
        }

        return TestData.Type(name, kind) with { Members = members };
    }

    /// <summary>Counts every markdown file under <paramref name="root"/>.</summary>
    /// <param name="root">Directory to walk recursively.</param>
    /// <returns>Total number of <c>.md</c> files found.</returns>
    private static int MarkdownFiles(string root) =>
        Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Count();
}
