// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="MemberUidProjection"/>: filters compiler-generated
/// (angle-bracket) members out, projects the survivors into a
/// pre-sized array of bare uids (M:/T:/etc. prefix stripped), and
/// alphabetises ordinal-string. Mirrors the docfx convention for the
/// <c>children:</c> field.
/// </summary>
public class MemberUidProjectionTests
{
    /// <summary>CountKept ignores names containing <c>&lt;</c> or <c>&gt;</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CountKeptIgnoresCompilerGeneratedMembers()
    {
        ApiMember[] members =
        [
            Member("Run", "M:Foo.Run"),
            Member("<>c__DisplayClass0_0", "M:Foo.<>c__DisplayClass0_0"),
            Member("Walk", "M:Foo.Walk"),
        ];

        await Assert.That(MemberUidProjection.CountKept(members)).IsEqualTo(2);
    }

    /// <summary>CollectSortedChildUids returns an empty array when every member is compiler-generated.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectReturnsEmptyWhenAllFiltered()
    {
        ApiMember[] members =
        [
            Member("<>c__DisplayClass0_0", "M:Foo.<>c__DisplayClass0_0"),
            Member("<MoveNext>d__1", "M:Foo.<MoveNext>d__1"),
        ];

        var uids = MemberUidProjection.CollectSortedChildUids(members);

        await Assert.That(uids.Length).IsEqualTo(0);
    }

    /// <summary>Surviving uids are stripped of their prefix and ordinal-sorted.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectStripsPrefixAndSortsOrdinal()
    {
        ApiMember[] members =
        [
            Member("Zeta", "M:Foo.Zeta"),
            Member("Alpha", "M:Foo.Alpha"),
            Member("Mu", "M:Foo.Mu"),
            Member("<>c", "M:Foo.<>c"),
        ];

        var uids = MemberUidProjection.CollectSortedChildUids(members);

        string[] expected = ["Foo.Alpha", "Foo.Mu", "Foo.Zeta"];
        await Assert.That(uids).IsEquivalentTo(expected);
    }

    /// <summary>Builds an ApiMember with only the fields the projection inspects (Name + Uid).</summary>
    /// <param name="name">Metadata name.</param>
    /// <param name="uid">Documentation comment ID.</param>
    /// <returns>The fabricated member.</returns>
    private static ApiMember Member(string name, string uid) => new(
        Name: name,
        Uid: uid,
        Kind: ApiMemberKind.Method,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: string.Empty,
        Parameters: [],
        TypeParameters: [],
        ReturnType: null,
        ContainingTypeUid: string.Empty,
        ContainingTypeName: string.Empty,
        SourceUrl: null,
        Documentation: ApiDocumentation.Empty,
        IsObsolete: false,
        ObsoleteMessage: null,
        Attributes: []);
}
