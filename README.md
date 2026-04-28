
[![CI Build](https://github.com/glennawatson/SourceDocParserLib/actions/workflows/ci.yml/badge.svg)](https://github.com/glennawatson/SourceDocParserLib/actions/workflows/ci.yml) [![Coverage](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=coverage)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib) [![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib) [![Duplicated Lines (%)](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=duplicated_lines_density)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib) [![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib) [![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib)
# SourceDocParserLib

Roslyn-based .NET assembly walker that turns compiled `.dll` + `.pdb` + `.xml` triples into a strongly-typed API catalog (types, members, signatures, XML docs, inheritdoc, SourceLink) and hands it to a pluggable emitter for rendering.

The catalog is **format-neutral**. Emitters decide how to render it — Markdown for Zensical / mkdocs Material, or YAML for docfx ManagedReference, with room for other targets.

## Packages

| Package | What it does |
|---|---|
| `SourceDocParser` | Core walker, merger, source-link resolution. Defines `IAssemblySource`, `IDocumentationEmitter`, `IMetadataExtractor`, the `ICrefResolver` cross-link seam, and the shared `CatalogIndexes` rollup (derived classes / extension methods / inherited members). |
| `SourceDocParser.NuGet` | `IAssemblySource` that fetches packages from `nuget.org` by owner / explicit list and exposes the per-TFM `lib/` trees. |
| `SourceDocParser.Zensical` | `IDocumentationEmitter` that writes Markdown tuned for Zensical / mkdocs Material (admonitions, content tabs, mermaid). |
| `SourceDocParser.Docfx` | `IDocumentationEmitter` that writes docfx ManagedReference YAML pages (drop-in replacement for `dotnet docfx metadata` output) plus the `docfx.json` config-file shim that lets an existing docfx site drive the parser pipeline. |

Logging flows through `Microsoft.Extensions.Logging.Abstractions` source-generated `[LoggerMessage]` partials, so any host (Serilog, Console, NLog, …) plugs in without the libraries taking a dependency on a specific backend.

## Quick start

```csharp
var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

var source = new NuGetAssemblySource(
    rootDirectory: "/path/to/repo",   // contains nuget-packages.json
    apiPath:       "/path/to/api",    // where lib/ + refs/ get extracted
    logger:        loggerFactory.CreateLogger<NuGetAssemblySource>());

var emitter = new ZensicalDocumentationEmitter();

var result = await new MetadataExtractor().RunAsync(
    source,
    outputRoot: "/path/to/markdown-output",
    emitter,
    loggerFactory.CreateLogger<MetadataExtractor>());

Console.WriteLine($"Emitted {result.PagesEmitted} pages across {result.CanonicalTypes} types.");
```

## Performance

The pipeline is built around a span-based XML scanner, pooled buffers, eager release of memory-mapped reference DLLs, and a streaming type merger that consumes catalogs as they land. The result is a small, predictable allocation budget and a fast wall-time per assembly.

**Benchmark workload.** Numbers below are from the BenchmarkDotNet suite under `src/benchmarks/`, run on a Ryzen 7 5800X / .NET 10. The workload extracts three real NuGet packages from `nuget.org` — pulling each package's `lib/` and `ref/` trees and the matching reference assemblies, walking every public symbol across ~19 target-framework groups, parsing the shipped XML doc files for each assembly, resolving `<inheritdoc/>` chains, and emitting roughly 600 canonical type pages after cross-TFM merge. The local NuGet cache is warmed once during global setup so per-iteration timings measure the walk + merge + emit pipeline, not the network leg.

**End-to-end (`MetadataExtractor.RunAsync`):**

| Phase                                  | Wall time | Allocated |
|----------------------------------------|----------:|----------:|
| Full pipeline (`RunAsync`)             |    ~1.4 s |   ~650 MB |
| Discover (NuGet config + cache scan)   |   ~660 ms |   ~240 MB |
| Load + walk (parallel, all groups)     |    ~1.5 s |   ~670 MB |
| Merge (cross-TFM dedup)                |      2 ms |   ~550 KB |
| Emit (Zensical Markdown)               |     79 ms |    ~63 MB |

Peak working set is bounded too: per-TFM compilation loaders dispose as soon as their last assembly finishes walking, so the memory-mapped BCL reference views are released eagerly instead of accumulating until `RunAsync` exits.

**Per-call hotspots:**

| Operation                                                                |    Time | Allocated |
|--------------------------------------------------------------------------|--------:|----------:|
| `XmlDocToMarkdown.Convert` — plain summary                               |  ~25 ns |     176 B |
| `XmlDocToMarkdown.Convert` — tagged with `<see>` / `<c>` / `<paramref>`  | ~786 ns |     304 B |
| `XmlDocToMarkdown.Convert` — code block + bullet list                    | ~1.0 µs |     440 B |
| `TfmResolver.FindBestRefsTfm` — exact match                              |   ~2 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` — platform-suffix strip                    |  ~11 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` — netstandard fallback                     | ~471 ns |     1 KB  |
| `TypeMerger.Merge` — 600 types × 3 TFMs                                  | ~115 µs |    325 KB |

**Emitter cost per type page** (no I/O, just markup formatting; baseline = Zensical Markdown):

| Workload (types × members/type) | Zensical Markdown   | DocFx YAML            | Time | Alloc |
|---------------------------------|--------------------:|----------------------:|-----:|------:|
| 100 × 5                         |   78 µs / 420 KB    |   305 µs / 1,410 KB   | 3.9× | 3.4×  |
| 100 × 30                        |  288 µs / 1,334 KB  | 1,618 µs / 6,184 KB   | 5.5× | 4.6×  |
| 600 × 5                         |  459 µs / 2,522 KB  | 1,823 µs / 8,461 KB   | 3.9× | 3.4×  |
| 600 × 30                        | 1,938 µs / 8,006 KB | 10,820 µs / 37,106 KB | 5.7× | 4.6×  |
| 2000 × 5                        | 1,617 µs / 8,406 KB | 7,443 µs / 28,203 KB  | 4.5× | 3.4×  |
| 2000 × 30                       | 8,528 µs / 26.7 MB  | 37,166 µs / 123.7 MB  | 4.4× | 4.6×  |

DocFx YAML is heavier by design — every member duplicates uid / commentId / parent / name / nameWithType / fullName, and the page-level `references:` list adds another mapping per cross-referenced type. The emitter still hand-writes its YAML directly via `StringBuilder` (no YamlDotNet runtime dependency), with a single-allocation fast path for the qualified-name composites (`type.Name + "." + member.Name`) that round-trips identifiers as plain scalars when escape-safe.

**Side-by-side against `dotnet docfx metadata`.** Two fully isolated standalone benchmark assemblies — `benchmarks/Docfx.StandaloneBenchmarks/` (calls `DotnetApiCatalog.GenerateManagedReferenceYamlFiles` in-process) and `benchmarks/SourceDocParser.Docfx.StandaloneBenchmarks/` (drives our pipeline through `DocfxYamlEmitter`) — both target the same 4 NuGet packages (`ReactiveUI`, `Splat`, `DynamicData`, `System.Reactive`), measured by BenchmarkDotNet's `[ShortRunJob]` on the same machine:

| Pipeline                                                       | Mean    | Allocated |
|----------------------------------------------------------------|--------:|----------:|
| docfx 2.78.5 — `DotnetApiCatalog.GenerateManagedReferenceYamlFiles` | 1.598 s |   6.72 MB |
| `SourceDocParser` + `DocfxYamlEmitter`                         | 2.031 s |  919.6 MB |

The two pipelines aren't strictly walking identical inputs — docfx loads a synthesised `Fixture.csproj` that pulls the 4 packages as transitive `PackageReference`s and walks one effective TFM, while our pipeline resolves every shipped `lib/`/`ref/` slice across ~19 supported TFMs from `nuget-packages.json` and merges across them. Working backward from that fixture difference, our per-TFM walk explains both the wall-time delta and the allocation gap (each TFM spins a fresh Roslyn compilation graph, and the cross-TFM merger holds catalogs while it dedupes UIDs). The contract pinned by the comparison is parity output (every `T:`, `M:`, `P:`, `E:` UID docfx emits, our pipeline emits too) at the per-page emit cost shown in the per-page table above.

### Strategies the pipeline uses

- **Custom span-based XML scanner.** Every NuGet package ships an `<assembly-name>.xml` doc file alongside its `.dll`, holding the `///` doc comments for every public symbol. The walker has to read each member's XML fragment per symbol, render its `<see>` / `<c>` / `<list>` / `<inheritdoc>` tags into Markdown, and do the same again per `<param>` / `<exception>` inside it — for thousands of symbols per assembly. `XmlReader` works for that, but its `XmlTextReaderImpl` allocates multi-KB internal buffers (`NodeData[]`, `NamespaceManager`, char buffers, `Entry[]`) per construction, which dominates the doc-parse profile. So the pipeline ships a small `ref struct DocXmlScanner` that walks the doc text directly over `ReadOnlySpan<char>` and implements just the XML grammar that `///` doc comments actually use. Both the per-symbol parser and the Markdown renderer drive the scanner, so per-element XML processing is allocation-free apart from the result string.
- **Build-once-then-read-many `XmlDocSource`.** Each `.xml` doc file is read once via `File.ReadAllBytes` + `Encoding.UTF8.GetString`, then indexed by per-member `(offset, length)` ranges. The substring is only materialised when a consumer calls `Get(memberId)`, and the source is safe for concurrent reads under the parallel walker.
- **Eager per-group loader disposal.** Each TFM group has its own `CompilationLoader` with a private `MetadataReferenceCache` holding memory-mapped views of every reference DLL. As soon as the last assembly in a group finishes its walk, an interlocked counter drops to zero and the loader disposes — peak working set scales with the slowest-finishing group, not the total number of groups times their references.
- **Streaming type merger.** The parallel walk feeds `ApiCatalog`s into `StreamingTypeMerger` one at a time and immediately drops its reference, instead of accumulating every catalog in a `ConcurrentBag` until the walk phase finishes.
- **Capture-free parallel dispatch.** The `Parallel.ForEachAsync` lambda is `static` — every dependency it touches is bundled into a `WalkContext` record attached to each work item, so dispatch never allocates a closure object per assembly.
- **Pooled `StringBuilder` on the converter.** `XmlDocToMarkdown` is per-walk by construction; reusing a single builder across every `Convert` call eliminates the per-element allocation that would otherwise dominate the renderer.
- **Emit-time doc rendering with a pluggable cref resolver.** The walker hands the catalog over with `ApiDocumentation` strings carrying *raw XML doc fragments*, not pre-rendered Markdown. Each emitter constructs its own `XmlDocToMarkdown(ICrefResolver)` and folds rendering over the catalog via `RenderedTypeFactory.Render(type, converter)` just before emit, so Zensical (mkdocs-autorefs `[name][uid]` form, with arity backticks translated to hyphens to match its anchors) and docfx (`<xref:UID>` form) produce wire-correct cross-links from the same catalog without the walker baking either format in. `DefaultCrefResolver` provides the fallback for tools that don't ship a custom resolver.
- **Shared `CatalogIndexes` rollup.** Derived-class lookup, reverse extension-method lookup, and per-type inherited-member uid lists are built once per emit run in a single O(N) sweep and frozen via `FrozenDictionary`. Each emitter passes its own `System.Object` baseline UIDs (docfx wants bare names, Zensical wants `M:`-prefixed commentIds) so the algorithm stays shared while wire format stays per-emitter.
- **Pre-sized buffers.** Each nupkg zip entry is sized to its known uncompressed length up front so the backing `byte[]` is allocated once at the right size instead of doubling-and-copying on every `Write`. SourceLink URL rewriting fuses the base URL and the line anchor into one interpolated-string handler call so the GitHub / Bitbucket / GitLab / Azure DevOps blob URL is materialised in a single `string`.

## Repository layout

```
SourceDocParserLib/
  src/
    SourceDocParser/
    SourceDocParser.NuGet/
    SourceDocParser.Docfx/
    SourceDocParser.Zensical/
    tests/
      SourceDocParser.Tests/             unit tests (TUnit)
      SourceDocParser.IntegrationTests/  end-to-end + Zensical render-smoke
    Directory.Build.props                shared lib config
    Directory.Packages.props             central package versions
    SourceDocParserLib.slnx
  Directory.Build.props
  version.json                           Nerdbank.GitVersioning
  .editorconfig
  stylecop.json
```

`dotnet build` from `src/` packs every non-test project into `artifacts/packages/` automatically (`<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`). Consumers in other repos can wire that directory up as a local feed via `nuget.config` until the libraries are published.

## Acknowledgements

The metadata extraction pipeline is inspired by — and lifts patterns from — [dotnet/docfx](https://github.com/dotnet/docfx) (MIT licensed). docfx's Roslyn-based assembly walker, inheritdoc resolution, and overall metadata model shaped this library's design. See [`LICENSE`](./LICENSE) for the original docfx attribution.

Built on:

- [Roslyn](https://github.com/dotnet/roslyn) (Microsoft.CodeAnalysis.CSharp) for compilation + symbol model
- [ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy) for transitive reference resolution
- [NuGet.Frameworks](https://github.com/NuGet/NuGet.Client) + [NuGet.Versioning](https://github.com/NuGet/NuGet.Client) for proper TFM compatibility and SemVer ordering
- [Polly v8](https://github.com/App-vNext/Polly) for HTTP retry/rate-limit pipelines

## License

MIT — see [`LICENSE`](./LICENSE) for the full text and the docfx attribution.
