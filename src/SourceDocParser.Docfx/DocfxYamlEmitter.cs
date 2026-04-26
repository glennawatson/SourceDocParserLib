// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Docfx;

/// <summary>
/// <see cref="IDocumentationEmitter"/> implementation that renders the
/// merged catalog as docfx ManagedReference YAML — one <c>.yml</c> file
/// per type, holding the full PageViewModel shape (the type plus its
/// members in <c>items:</c>, plus referenced types in <c>references:</c>).
/// Output matches what docfx's own metadata extractor produces, so the
/// generated tree drops straight into a docfx <c>build.content</c>
/// section as a replacement for <c>dotnet docfx metadata</c>.
/// </summary>
/// <remarks>
/// Hand-rolled writer over <see cref="StringBuilder"/>: scalars are
/// appended directly with an inline escape probe, indentation is fixed
/// at two spaces per level, and builder helpers return the same builder
/// so emit sequences read top-to-bottom as fluent chains.
/// </remarks>
public sealed class DocfxYamlEmitter : IDocumentationEmitter
{
    /// <summary>Docfx's YamlMime header for ManagedReference pages.</summary>
    public const string YamlMimeHeader = "### YamlMime:ManagedReference";

    /// <summary>File extension docfx expects for ManagedReference pages.</summary>
    public const string FileExtension = ".yml";

    /// <summary>
    /// Initial StringBuilder capacity per page. ~4 KB matches a typical
    /// type page once members are folded in and saves the first couple
    /// of buffer doublings.
    /// </summary>
    private const int InitialPageCapacity = 4096;

    /// <summary>
    /// Renders a single docfx ManagedReference page as a YAML string —
    /// header, items list (the type and its members), and references
    /// list pointing at types the page mentions.
    /// </summary>
    /// <param name="type">Type whose page to render.</param>
    /// <returns>The full YAML page text.</returns>
    public static string Render(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return new StringBuilder(InitialPageCapacity)
            .Append(YamlMimeHeader).Append('\n')
            .Append("items:\n")
            .AppendTypeItem(type)
            .AppendMemberItems(type)
            .AppendPageReferences(CollectReferences(type))
            .ToString();
    }

    /// <summary>
    /// Returns the relative path for the type's <c>.yml</c> file. Docfx's
    /// own emitter uses one file per type with the file stem matching
    /// the type's UID, so we follow that convention to keep the output
    /// drop-in compatible.
    /// </summary>
    /// <param name="type">Type whose page path to compute.</param>
    /// <returns>The path relative to the output root.</returns>
    public static string PathFor(ApiType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var stem = type.Uid.Length > 0 ? DocfxCommentId.ToUid(type.Uid) : type.FullName;
        return DocfxInternalHelpers.SanitiseFileStem(stem) + FileExtension;
    }

    /// <inheritdoc />
    public async Task<int> EmitAsync(ApiType[] types, string outputRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var pages = 0;
        for (var i = 0; i < types.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = types[i];
            var fullPath = Path.Combine(outputRoot, PathFor(type));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, Render(type), cancellationToken).ConfigureAwait(false);
            pages++;
        }

        return pages;
    }

    /// <summary>
    /// Builds the deduplicated reference list a page needs — base type,
    /// declared interfaces, parameter and return types of every member,
    /// plus enum underlying type / delegate signature types / union
    /// case types as appropriate to the derivation.
    /// </summary>
    /// <param name="type">Type whose references to collect.</param>
    /// <returns>Distinct references in declaration order.</returns>
    internal static ApiTypeReference[] CollectReferences(ApiType type)
    {
        var references = new List<ApiTypeReference>(capacity: 8 + (type.Interfaces.Length * 2));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (type.BaseType is { } baseRef)
        {
            AddReference(references, seen, baseRef);
        }

        for (var i = 0; i < type.Interfaces.Length; i++)
        {
            AddReference(references, seen, type.Interfaces[i]);
        }

        var members = MembersOf(type);
        if (members is not null)
        {
            for (var i = 0; i < members.Length; i++)
            {
                CollectMemberReferences(members[i], references, seen);
            }
        }

        CollectKindSpecificReferences(type, references, seen);
        return [.. references];
    }

    /// <summary>
    /// Returns the docfx <c>type</c> field value for a top-level type
    /// (Class / Struct / Interface / Enum / Delegate / Class for unions).
    /// </summary>
    /// <param name="type">Type to map.</param>
    /// <returns>The docfx-style type label.</returns>
    internal static string MemberTypeForType(ApiType type) => type switch
    {
        ApiEnumType => "Enum",
        ApiDelegateType => "Delegate",
        ApiObjectType { Kind: ApiObjectKind.Struct or ApiObjectKind.RecordStruct } => "Struct",
        ApiObjectType { Kind: ApiObjectKind.Interface } => "Interface",
        _ => "Class",
    };

    /// <summary>
    /// Returns the docfx <c>type</c> field value for a member kind.
    /// Mirrors the strings docfx's own metadata extractor produces.
    /// </summary>
    /// <param name="kind">Member kind.</param>
    /// <returns>The docfx-style member-type label.</returns>
    internal static string MemberTypeForKind(ApiMemberKind kind) => kind switch
    {
        ApiMemberKind.Constructor => "Constructor",
        ApiMemberKind.Property => "Property",
        ApiMemberKind.Field => "Field",
        ApiMemberKind.Method => "Method",
        ApiMemberKind.Operator => "Operator",
        ApiMemberKind.Event => "Event",
        ApiMemberKind.EnumValue => "Field",
        _ => "Member",
    };

    /// <summary>
    /// Returns the member list a type carries, or <see langword="null"/>
    /// for kinds without a flat member surface (enums and delegates).
    /// </summary>
    /// <param name="type">Type to inspect.</param>
    /// <returns>The member list, or <see langword="null"/>.</returns>
    private static ApiMember[]? MembersOf(ApiType type) => type switch
    {
        ApiObjectType o => o.Members,
        ApiUnionType u => u.Members,
        _ => null,
    };

    /// <summary>
    /// Adds the return type and every parameter type of <paramref name="member"/>
    /// to the page-level reference list.
    /// </summary>
    /// <param name="member">Member whose referenced types to collect.</param>
    /// <param name="references">Accumulator to append into.</param>
    /// <param name="seen">Dedup set keyed on UID / display name.</param>
    private static void CollectMemberReferences(ApiMember member, List<ApiTypeReference> references, HashSet<string> seen)
    {
        if (member.ReturnType is { } ret)
        {
            AddReference(references, seen, ret);
        }

        for (var p = 0; p < member.Parameters.Length; p++)
        {
            AddReference(references, seen, member.Parameters[p].Type);
        }
    }

    /// <summary>
    /// Routes <paramref name="type"/> through the kind-specific
    /// reference-collector for its derived type — enums add their
    /// underlying type, delegates add Invoke signature types, unions add
    /// each case type.
    /// </summary>
    /// <param name="type">Type to inspect.</param>
    /// <param name="references">Accumulator to append into.</param>
    /// <param name="seen">Dedup set keyed on UID / display name.</param>
    private static void CollectKindSpecificReferences(ApiType type, List<ApiTypeReference> references, HashSet<string> seen)
    {
        switch (type)
        {
            case ApiEnumType e:
            {
                AddReference(references, seen, e.UnderlyingType);
                break;
            }

            case ApiDelegateType d:
            {
                CollectDelegateReferences(d, references, seen);
                break;
            }

            case ApiUnionType u:
            {
                CollectUnionReferences(u, references, seen);
                break;
            }
        }
    }

    /// <summary>Adds the return type and parameter types of a delegate's Invoke signature.</summary>
    /// <param name="type">Delegate type.</param>
    /// <param name="references">Accumulator to append into.</param>
    /// <param name="seen">Dedup set keyed on UID / display name.</param>
    private static void CollectDelegateReferences(ApiDelegateType type, List<ApiTypeReference> references, HashSet<string> seen)
    {
        if (type.Invoke.ReturnType is { } ret)
        {
            AddReference(references, seen, ret);
        }

        for (var p = 0; p < type.Invoke.Parameters.Length; p++)
        {
            AddReference(references, seen, type.Invoke.Parameters[p].Type);
        }
    }

    /// <summary>Adds each case type from a union's <see cref="ApiUnionType.Cases"/> list.</summary>
    /// <param name="type">Union type.</param>
    /// <param name="references">Accumulator to append into.</param>
    /// <param name="seen">Dedup set keyed on UID / display name.</param>
    private static void CollectUnionReferences(ApiUnionType type, List<ApiTypeReference> references, HashSet<string> seen)
    {
        for (var c = 0; c < type.Cases.Length; c++)
        {
            AddReference(references, seen, type.Cases[c]);
        }
    }

    /// <summary>
    /// Adds <paramref name="reference"/> only the first time we've seen
    /// the keying string (UID if present, display name otherwise) —
    /// keeps the page-level list distinct without sorting it.
    /// </summary>
    /// <param name="references">Accumulator to append into.</param>
    /// <param name="seen">Dedup set keyed on UID / display name.</param>
    /// <param name="reference">Candidate to add.</param>
    private static void AddReference(List<ApiTypeReference> references, HashSet<string> seen, ApiTypeReference reference)
    {
        var key = reference.Uid.Length > 0 ? reference.Uid : reference.DisplayName;
        if (!seen.Add(key))
        {
            return;
        }

        references.Add(reference);
    }
}
