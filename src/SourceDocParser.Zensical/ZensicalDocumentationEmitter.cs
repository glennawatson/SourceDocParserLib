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
    public Task<int> EmitAsync(ApiType[] types, string outputRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var pages = 0;
        var hasRouting = _options.PackageRouting.Length > 0;
        for (var i = 0; i < types.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = types[i];

            // Walk-scope filter: when the user has configured
            // package routing, types from non-primary assemblies
            // (transient deps, BCL) are skipped — they get
            // referenced via cross-links to Microsoft Learn /
            // repo search instead of getting their own pages.
            if (hasRouting && PackageRouter.ResolveFolder(type.AssemblyName, _options.PackageRouting) is null)
            {
                continue;
            }

            // Compiler-generated artefacts (display classes, async
            // state machines, anonymous types) are name-mangled with
            // angle brackets in metadata. Match docfx's heuristic and
            // skip them rather than emitting nonsensical pages.
            if (IsCompilerGenerated(type.Name))
            {
                continue;
            }

            TypePageEmitter.RenderToFile(type, outputRoot, _options);
            pages++;
            pages += EmitMemberPages(type, outputRoot, _options);
        }

        pages += LandingPageEmitter.EmitAll(types, outputRoot, _options);

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
    /// <param name="options">Routing + cross-link tunables threaded through to the per-overload write.</param>
    /// <returns>Total page count written.</returns>
    private static int EmitMemberPages(ApiType type, string outputRoot, ZensicalEmitterOptions options)
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
            MemberPageEmitter.RenderToFile(type, group.Key, [.. group.Value], outputRoot, options);
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
