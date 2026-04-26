// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical;

/// <summary>
/// <see cref="IDocumentationEmitter"/> implementation that renders the
/// merged catalog as a flat tree of Zensical/mkdocs Material Markdown
/// pages — one page per type, plus one overload-group page per
/// distinct member name. Sequential write loop: the merge pass already
/// happened so there's no concurrency benefit, and File.WriteAllText
/// over ~30k small files is plenty fast.
/// </summary>
public sealed class ZensicalDocumentationEmitter : IDocumentationEmitter
{
    /// <inheritdoc />
    public Task<int> EmitAsync(List<ApiType> types, string outputRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var pages = 0;
        for (var i = 0; i < types.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = types[i];
            TypePageEmitter.RenderToFile(type, outputRoot);
            pages++;
            pages += EmitMemberPages(type, outputRoot);
        }

        return Task.FromResult(pages);
    }

    /// <summary>
    /// Emits one Markdown page per overload group on the supplied type.
    /// Only object and union types contribute member pages — enums and
    /// delegates are typed away from this code path entirely, so there's
    /// no per-call kind check needed. Members are bucketed by name;
    /// the bucket dictionary is pre-sized to the member count, which
    /// is a tight upper bound on the distinct names.
    /// </summary>
    /// <param name="type">Type whose members to emit pages for.</param>
    /// <param name="outputRoot">Markdown output root.</param>
    /// <returns>Total page count written.</returns>
    private static int EmitMemberPages(ApiType type, string outputRoot)
    {
        var members = type switch
        {
            ApiObjectType o => o.Members,
            ApiUnionType u => u.Members,
            _ => null,
        };

        if (members is not { Count: > 0 })
        {
            return 0;
        }

        var groups = new Dictionary<string, List<ApiMember>>(capacity: members.Count, StringComparer.Ordinal);
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (!groups.TryGetValue(member.Name, out var bucket))
            {
                bucket = [];
                groups[member.Name] = bucket;
            }

            bucket.Add(member);
        }

        var pages = 0;
        foreach (var group in groups)
        {
            MemberPageEmitter.RenderToFile(type, group.Key, group.Value, outputRoot);
            pages++;
        }

        return pages;
    }
}
