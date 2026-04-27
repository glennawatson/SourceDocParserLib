// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Frozen;
using SourceDocParser.Model;
using SourceDocParser.XmlDoc;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;
using SourceDocParser.Zensical.Routing;

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
    /// <summary>Emitter tunables (per-package routing, BCL link base URL).</summary>
    private readonly ZensicalEmitterOptions _options;

    /// <summary>Initializes a new instance of the <see cref="ZensicalDocumentationEmitter"/> class with the legacy flat-namespace layout.</summary>
    public ZensicalDocumentationEmitter()
        : this(ZensicalEmitterOptions.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ZensicalDocumentationEmitter"/> class.</summary>
    /// <param name="options">Routing rules + cross-link tunables.</param>
    public ZensicalDocumentationEmitter(ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public Task<int> EmitAsync(ApiType[] types, string outputRoot) =>
        EmitAsync(types, outputRoot, CancellationToken.None);

    /// <inheritdoc />
    public Task<int> EmitAsync(ApiType[] types, string outputRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var indexes = ZensicalCatalogIndexes.Build(types);
        var emittedUids = BuildEmittedUidSet(types, _options);
        var resolver = new ZensicalCrefResolver(emittedUids, _options);
        var converter = new XmlDocToMarkdown(resolver);
        var runOptions = _options with { Resolver = resolver };
        var context = new ZensicalEmitContext(runOptions, indexes, emittedUids, converter);

        var pages = WriteTypeAndMemberPages(types, outputRoot, context, cancellationToken);
        pages += LandingPageEmitter.EmitAll(types, outputRoot, context);
        return Task.FromResult(pages);
    }

    /// <summary>
    /// Builds the set of UIDs the emitter is producing pages for —
    /// types that survive the routing + compiler-generated filter,
    /// plus the UIDs of every non-compiler-generated member on each
    /// surviving type (for object and union shapes), plus enum-value
    /// UIDs. The Zensical cref resolver consults this set so cref
    /// references that point at non-emitted symbols fall through to
    /// inline code instead of broken autoref links.
    /// </summary>
    /// <param name="types">All canonical types about to be considered for emission.</param>
    /// <param name="options">Routing + cross-link tunables.</param>
    /// <returns>The frozen set of UIDs the emitter will produce anchors for.</returns>
    internal static FrozenSet<string> BuildEmittedUidSet(ApiType[] types, ZensicalEmitterOptions options)
    {
        var hasRouting = options.PackageRouting is [_, ..];
        var collected = new HashSet<string>(types.Length, StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (ShouldSkipType(type, hasRouting, options))
            {
                continue;
            }

            if (type.Uid is { Length: > 0 })
            {
                collected.Add(type.Uid);
            }

            CollectMemberUids(type, collected);
        }

        return collected.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Walks <paramref name="types"/>, applies the routing + compiler-
    /// generated filter, and writes the type and member pages for
    /// each survivor. Split out of
    /// <see cref="EmitAsync(ApiType[], string, CancellationToken)"/>
    /// so the orchestrator method stays at low cyclomatic complexity.
    /// </summary>
    /// <param name="types">All canonical types about to be considered for emission.</param>
    /// <param name="outputRoot">The Markdown output root.</param>
    /// <param name="context">Render context built once for the run.</param>
    /// <param name="cancellationToken">Cancellation token observed between types.</param>
    /// <returns>The page count for the type and member emit phase.</returns>
    private static int WriteTypeAndMemberPages(
        ApiType[] types,
        string outputRoot,
        ZensicalEmitContext context,
        CancellationToken cancellationToken)
    {
        var pages = 0;
        var hasRouting = context.Options.PackageRouting is [_, ..];
        for (var i = 0; i < types.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = types[i];
            if (ShouldSkipType(type, hasRouting, context.Options))
            {
                continue;
            }

            TypePageEmitter.RenderToFile(type, outputRoot, context);
            pages++;
            pages += EmitMemberPages(type, outputRoot, context);
        }

        return pages;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> should be skipped:
    /// either it's outside the configured package routing scope or
    /// its name is a compiler-generated artefact.
    /// </summary>
    /// <param name="type">Candidate type.</param>
    /// <param name="hasRouting">Whether package routing is configured.</param>
    /// <param name="options">Emitter options carrying the routing rules.</param>
    /// <returns>True when the type should be skipped.</returns>
    private static bool ShouldSkipType(ApiType type, bool hasRouting, ZensicalEmitterOptions options)
    {
        if (hasRouting && PackageRouter.ResolveFolder(type.AssemblyName, options.PackageRouting) is null)
        {
            return true;
        }

        return IsCompilerGenerated(type.Name);
    }

    /// <summary>
    /// Adds the UIDs of <paramref name="type"/>'s members (non-
    /// compiler-generated) and enum values to <paramref name="collected"/>.
    /// </summary>
    /// <param name="type">Owning type.</param>
    /// <param name="collected">Destination UID set.</param>
    private static void CollectMemberUids(ApiType type, HashSet<string> collected)
    {
        switch (type)
        {
            case ApiObjectType obj:
                {
                    AddMemberUids(obj.Members, collected);
                    return;
                }

            case ApiUnionType union:
                {
                    AddMemberUids(union.Members, collected);
                    return;
                }

            case ApiEnumType enumType:
                {
                    AddEnumValueUids(enumType.Values, collected);
                    return;
                }

            default:
                return;
        }
    }

    /// <summary>Adds non-compiler-generated member UIDs to <paramref name="collected"/>.</summary>
    /// <param name="members">Members to scan.</param>
    /// <param name="collected">Destination UID set.</param>
    private static void AddMemberUids(ApiMember[] members, HashSet<string> collected)
    {
        for (var i = 0; i < members.Length; i++)
        {
            var m = members[i];
            if (!IsCompilerGenerated(m.Name) && m.Uid is { Length: > 0 })
            {
                collected.Add(m.Uid);
            }
        }
    }

    /// <summary>Adds enum-value UIDs to <paramref name="collected"/>.</summary>
    /// <param name="values">Enum values to scan.</param>
    /// <param name="collected">Destination UID set.</param>
    private static void AddEnumValueUids(ApiEnumValue[] values, HashSet<string> collected)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var v = values[i];
            if (v.Uid is { Length: > 0 })
            {
                collected.Add(v.Uid);
            }
        }
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
    /// <param name="context">Render context — supplies routing options + the doc converter.</param>
    /// <returns>Total page count written.</returns>
    private static int EmitMemberPages(ApiType type, string outputRoot, ZensicalEmitContext context)
    {
        var members = type switch
        {
            ApiObjectType o => o.Members,
            ApiUnionType u => u.Members,
            _ => null,
        };

        if (members is not { Length: > 0 })
        {
            return 0;
        }

        var groups = new Dictionary<string, List<ApiMember>>(capacity: members.Length, StringComparer.Ordinal);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];

            // Property and event accessors are emitted by Roslyn as
            // standalone methods named `<get_Foo>k__BackingField` /
            // `get_Foo` etc. The angle-bracketed forms catch backing
            // fields and similar artefacts; the get_/set_/add_/remove_
            // pattern catches accessors which the property/event page
            // already covers.
            if (IsCompilerGenerated(member.Name))
            {
                continue;
            }

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
            MemberPageEmitter.RenderToFile(type, group.Key, [.. group.Value], outputRoot, context);
            pages++;
        }

        return pages;
    }

    /// <summary>
    /// Tests whether a metadata <paramref name="symbolName"/> is a
    /// compiler-generated artefact that shouldn't surface as a doc
    /// page. Mirrors the docfx heuristic: any name containing an
    /// angle bracket is a mangled identifier (display classes,
    /// async / iterator state machines, anonymous types, lambda
    /// closures, backing fields).
    /// </summary>
    /// <param name="symbolName">The symbol's metadata name.</param>
    /// <returns>True when the symbol should be skipped.</returns>
    private static bool IsCompilerGenerated(string symbolName) =>
        symbolName.AsSpan().IndexOfAny('<', '>') >= 0;
}
