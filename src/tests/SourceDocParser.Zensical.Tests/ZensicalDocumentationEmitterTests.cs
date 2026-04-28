// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

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
