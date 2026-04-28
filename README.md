
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

## Supported target frameworks

The walker resolves NuGet packages against frameworks that the active .NET SDK still understands and that Microsoft is still shipping fixes for:

- **Modern .NET (5.0+)** — `net5.0`, `net6.0`, `net7.0`, `net8.0`, `net9.0`, `net10.0`, plus the `net*-android`, `net*-ios`, `net*-maccatalyst`, `net*-windows` workload variants. See the official [.NET and .NET Core support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
- **netstandard** — `netstandard1.0` through `netstandard2.1`. Sticks around because the BCL targets it, even though [no future netstandard releases are planned](https://learn.microsoft.com/en-us/dotnet/standard/net-standard).
- **.NET Framework, net462 and newer** — `net462`, `net47`, `net471`, `net472`, `net48`, `net481`. net462 is the floor that supports `netstandard2.0` type forwards and ships ref packs in modern SDKs. See the [.NET Framework support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework) for which of those are still in mainstream / extended support.

**Out of scope (legacy, not supported):**

| Family | Examples | Why |
|---|---|---|
| Xamarin | `xamarinios*`, `xamarinmac*`, `xamarintvos*`, `xamarinwatchos*` | [Support ended 1 May 2024](https://dotnet.microsoft.com/en-us/platform/support/policy/xamarin); the workloads moved to [.NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/what-is-maui) under the modern `net*-android` / `net*-ios` / `net*-maccatalyst` / `net*-tvos` TFMs. |
| Legacy Mono profiles | `MonoAndroid*`, `MonoTouch*` | Predecessors of the Xamarin workloads. Same end-of-support story. |
| .NET Framework < 4.6.2 | `net20`, `net35`, `net40`, `net45`, `net451`, `net46`, `net461` | Out of [mainstream support](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework), and pre-net462 doesn't carry netstandard 2.0 type forwards so the resolver can't reuse modern surface against them. |
| Silverlight | `sl*` | Microsoft retired Silverlight on [12 October 2021](https://learn.microsoft.com/en-us/lifecycle/products/silverlight-5). |
| Windows Phone | `wp*`, `wpa*` | [Windows Phone 8.1 end-of-support](https://learn.microsoft.com/en-us/lifecycle/announcements/windows-phone-8-1-end-of-support-announcement) was 11 July 2017; the platform itself was discontinued. |
| Windows Store / UAP | `win8`, `winrt*`, `uap*` | UWP apps are now expected to migrate to [Windows App SDK / WinUI 3](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/). |
| Portable Class Libraries | `portable-*` profiles | Replaced by netstandard a decade ago. |

Packages that ship **only** legacy TFMs are skipped at fetch time. The fetcher used to log this at warning level — on real-world walks (ReactiveUI / Avalonia / Splat surfaces) that meant tens of warnings per run for packages like `System.Net.Primitives` or `System.Globalization.Extensions` (whose entire TFM list is `MonoAndroid10, MonoTouch10, net45, xamarinios10, xamarinmac20, ...`). Those skips are now logged at **information** level so genuine TFM mismatches still stay visible. Set your logger filter to `Information` if you want to see the legacy-skip list during a build.

## Performance

**Benchmark workload.** Numbers below are from the BenchmarkDotNet suite under `src/benchmarks/SourceDocParser.Benchmarks/`, run on a Ryzen 7 5800X / .NET 10. The workload extracts three NuGet packages from `nuget.org` -- pulling each package's `lib/` and `ref/` trees and the matching reference assemblies, walking every public symbol across ~19 target-framework groups, parsing the shipped XML doc files, resolving `<inheritdoc/>` chains, and emitting roughly 600 canonical type pages after cross-TFM merge. The local NuGet cache is warmed once during global setup so per-iteration timings measure the walk + merge + emit pipeline, not the network leg.

**End-to-end (`MetadataExtractor.RunAsync`):**

| Phase                                | Wall time | Allocated |
|--------------------------------------|----------:|----------:|
| Full pipeline (`RunAsync`)           |   ~1.5 s  |  ~525 MB  |
| Discover (NuGet config + cache scan) |  ~990 ms  |  ~258 MB  |
| Load + walk (parallel, all groups)   |  ~509 ms  |  ~236 MB  |
| Merge (cross-TFM dedup)              |   ~1 ms   |  ~380 KB  |
| Emit (Zensical Markdown)             |  ~139 ms  |   ~39 MB  |

The walk phase walks one Roslyn compilation per package -- one canonical TFM per equivalence class. Other TFMs whose public-API surface is a subset of the canonical's are folded in via a `MetadataReader` probe that only enumerates type tokens, no symbol tree, no constructed types. The merger then broadcasts the canonical's walked types into each subset TFM so `ApiType.AppliesTo` still records every TFM the type applies to.

**Per-call hotspots:**

| Operation                                                                |    Time | Allocated |
|--------------------------------------------------------------------------|--------:|----------:|
| `XmlDocToMarkdown.Convert` -- plain summary                              |  ~24 ns |     176 B |
| `XmlDocToMarkdown.Convert` -- tagged with `<see>` / `<c>` / `<paramref>` | ~916 ns |     456 B |
| `XmlDocToMarkdown.Convert` -- code block + bullet list                   | ~1.2 µs |     440 B |
| `TfmResolver.FindBestRefsTfm` -- exact match                             |   ~3 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` -- platform-suffix strip                   |  ~11 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` -- netstandard fallback                    | ~496 ns |     1 KB  |
| `TypeMerger.Merge` -- 600 types x 3 TFMs                                 | ~115 µs |    358 KB |

**Emitter cost per type page** (no I/O, just markup formatting; baseline = Zensical Markdown):

| Workload (types x members/type) | Zensical Markdown   | DocFx YAML            | Time  | Alloc |
|---------------------------------|--------------------:|----------------------:|------:|------:|
| 100 x 5                         |   72 µs / 288 KB    |   618 µs / 1,366 KB   |  8.6x |  4.7x |
| 100 x 30                        |  263 µs / 763 KB    | 5,432 µs / 6,338 KB   | 20.7x |  8.3x |
| 600 x 5                         |  437 µs / 1,730 KB  | 3,605 µs / 8,198 KB   |  8.3x |  4.7x |
| 600 x 30                        | 1,505 µs / 4,580 KB | 17,122 µs / 38,025 KB | 11.4x |  8.3x |

DocFx YAML is heavier by design -- every member duplicates uid / commentId / parent / name / nameWithType / fullName, and the page-level `references:` list adds another mapping per cross-referenced type. The emitter hand-writes YAML through `StringBuilder` (no YamlDotNet runtime dependency), with a single-allocation fast path for qualified-name composites that round-trip identifiers as plain scalars when escape-safe.

### How perf and allocations stay low

- **MetadataReader probe + canonical-only Roslyn walk.** The walker only spins up one Roslyn compilation per package -- the canonical TFM picked by descending rank. Other TFMs whose public type set is a subset of the canonical's are detected via a `System.Reflection.Metadata.MetadataReader` probe (no symbol binding, no constructed-type allocation) and folded into `ApiType.AppliesTo` via a synthetic broadcast catalog that reuses the canonical's already-walked types. TFMs whose surface is *not* a subset still get a full Roslyn walk so removed-in-newer-TFM types stay in the catalog.
- **Custom span-based XML scanner.** A `ref struct DocXmlScanner` walks `///` doc fragments directly over `ReadOnlySpan<char>`, implementing just the XML grammar doc comments use. `XmlReader`'s `XmlTextReaderImpl` allocates multi-KB internal buffers (`NodeData[]`, `NamespaceManager`, char buffers) per construction; the scanner avoids that. Both the per-symbol parser and the Markdown renderer drive it, so per-element XML processing is allocation-free apart from the result string.
- **Build-once-then-read-many `XmlDocSource`.** Each `.xml` doc file is read once via `File.ReadAllBytes` + `Encoding.UTF8.GetString` and indexed by per-member `(offset, length)` ranges; substrings materialise only when a consumer calls `Get(memberId)`. Safe for concurrent reads from the parallel walker.
- **Eager per-group loader disposal.** Each TFM group's `CompilationLoader` holds memory-mapped views of every reference DLL. An interlocked counter retires the loader as soon as its last assembly finishes; peak working set scales with the slowest-finishing group, not the total number of groups times their references.
- **Streaming type merger.** The parallel walk feeds `ApiCatalog`s into `StreamingTypeMerger` one at a time and immediately drops the reference. Catalogs don't accumulate in a `ConcurrentBag` waiting for the walk phase to finish.
- **Capture-free parallel dispatch.** The `Parallel.ForEachAsync` lambda is `static`; every dependency it touches is bundled into a `WalkContext` record attached to each work item, so dispatch never allocates a closure object per assembly.
- **Lazy `RenderedDoc` facade for emit-time conversion.** Walker output carries raw inner-XML fragments. Each emitter constructs an `XmlDocToMarkdown(ICrefResolver)` and wraps each symbol's documentation in a `RenderedDoc` that converts each text-shaped field on first read, caches the result, and skips fields the page doesn't consume. Zensical and docfx pick their own cref form (`[name][uid]` autoref vs `<xref:uid>` / Microsoft Learn URL) without the walker baking either in.
- **Thread-static `PageBuilderPool`.** Each emit thread reuses one `StringBuilder` across page composition calls via a `using`-scoped rental; pages clear the builder between uses instead of allocating fresh.
- **`PageWriter` streams chunks to disk.** The composed `StringBuilder` flushes via `GetChunks()` straight through a UTF-8 encoder + `ArrayPool<byte>` buffer into an unbuffered `FileStream`. The whole-page string and the 64 KB BufferedFileStreamStrategy buffer never need to exist.
- **Shared `CatalogIndexes` rollup.** Derived-class lookup, reverse extension-method lookup, and per-type inherited-member uid lists are built once per emit run in a single O(N) sweep and frozen via `FrozenDictionary`. Each emitter passes its own `System.Object` baseline UIDs (docfx bare names, Zensical `M:`-prefixed commentIds) so the algorithm stays shared while the wire format stays per-emitter.
- **Pre-sized buffers and stackalloc paths.** nupkg zip entries size their backing `byte[]` to the known uncompressed length up front. SourceLink URL rewriting and `ZensicalCrefResolver`'s Microsoft Learn link composer build their result strings via `stackalloc` + `new string(span)` so the only heap allocation is the returned string itself.

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
