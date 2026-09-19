[![CI Build](https://github.com/glennawatson/SourceDocParserLib/actions/workflows/ci.yml/badge.svg)](https://github.com/glennawatson/SourceDocParserLib/actions/workflows/ci.yml)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=coverage)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib)
[![Duplicated Lines (%)](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=duplicated_lines_density)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib)
[![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib)
[![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=glennawatson_SourceDocParserLib&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=glennawatson_SourceDocParserLib)
[![NuGet](https://img.shields.io/nuget/v/SourceDocParser.svg?logo=nuget&label=SourceDocParser)](https://www.nuget.org/packages/SourceDocParser/)
[![Downloads](https://img.shields.io/nuget/dt/SourceDocParser.svg?logo=nuget&label=downloads)](https://www.nuget.org/packages/SourceDocParser/)
[![GitHub stars](https://img.shields.io/github/stars/glennawatson/SourceDocParserLib?style=social)](https://github.com/glennawatson/SourceDocParserLib/stargazers)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

<br>
<a href="https://github.com/glennawatson/SourceDocParserLib">
  <img width="160" height="160" src="https://raw.githubusercontent.com/glennawatson/SourceDocParserLib/main/icons/SourceDocParserIcon.png" alt="SourceDocParserLib">
</a>
<br>

# SourceDocParserLib

Roslyn-based .NET assembly walker that turns compiled `.dll` + `.pdb` + `.xml`
triples into a strongly-typed API catalog (types, members, signatures, XML
docs, `<inheritdoc/>`, SourceLink) and hands it to a pluggable
`IDocumentationEmitter` for rendering.

The catalog is **format-neutral**. Emitters decide how to render it — Markdown
for Zensical / mkdocs Material, or YAML for docfx ManagedReference, with room
for other targets. Pages flow through an `IPageSink` so callers can write to
disk (`FilePageSink`) or pipe straight into another async pipeline
(`CallbackPageSink`) without staging files on disk.

Our primary target is API documentation in MkDocs / Zensical sites. We make
opinionated choices about page structure, cross-TFM merging, and the packages
that receive pages. Our Docfx emitter adapts that catalog to ManagedReference
YAML. Docfx itself provides a broader documentation and site-building toolchain,
including conceptual content, templates, navigation, and cross-references.
See the [approach and benchmark comparison](src/benchmarks/README.md) for the
scope of each tool and the measured differences.

Logging flows through `Microsoft.Extensions.Logging.Abstractions`
source-generated `[LoggerMessage]` partials, so any host (Serilog, Console,
NLog, …) plugs in without the libraries taking a dependency on a specific
backend.

---

## Packages

Each package is shipped to NuGet independently; the badge tracks the current
published version.

### Core

| Package | NuGet | What |
|---|---|---|
| [`SourceDocParser`][Core] | [![ver][CoreV]][Core] | Walker, merger, source-link resolution. Defines `IAssemblySource`, `IDocumentationEmitter`, `IMetadataExtractor`, the `IPageSink` streaming contract (`FilePageSink` + `CallbackPageSink`), the `ICrefResolver` cross-link seam, and the shared `CatalogIndexes` rollup (derived classes / extension methods / inherited members). |
| [`SourceDocParser.Common`][Common] | [![ver][CommonV]][Common] | Shared primitives consumed by every other package: pooled `StringBuilder` rentals, span-based path / token helpers, allocation-free identifier formatting. No public API stability guarantee yet — pinned via the same MinVer baseline as the rest. |

### Assembly sources

| Package | NuGet | What |
|---|---|---|
| [`SourceDocParser.NuGet`][Pkg] | [![ver][PkgV]][Pkg] | `IAssemblySource` that discovers documentation roots by NuGet owner or explicit manifest entries and restores an independent dependency graph for each root and target framework. |

### Emitters

| Package | NuGet | Builder | What |
|---|---|---|---|
| [`SourceDocParser.Zensical`][Zen] | [![ver][ZenV]][Zen] | `new ZensicalDocumentationEmitter()` | Writes Markdown tuned for Zensical / mkdocs Material — admonitions, content tabs, mermaid, MD-style cross-links via the autoref UID convention. |
| [`SourceDocParser.Docfx`][Docfx] | [![ver][DocfxV]][Docfx] | `new DocfxYamlEmitter()` | Provides ManagedReference YAML from our API catalog and a `docfx.json` configuration bridge for Docfx integrations. |

---

## Quick start

```csharp
using Microsoft.Extensions.Logging;
using SourceDocParser;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.Zensical;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

using var source = new NuGetAssemblySource(
    rootDirectory: "/path/to/repo",   // contains nuget-packages.json
    apiPath:       "/path/to/api",    // stores per-root restore graphs
    logger:        loggerFactory.CreateLogger<NuGetAssemblySource>());

var emitter = new ZensicalDocumentationEmitter();

// File-rooted sink for the legacy on-disk shape …
var sink = new FilePageSink("/path/to/markdown-output");

var result = await new MetadataExtractor().RunAsync(
    source,
    sink,
    emitter,
    loggerFactory.CreateLogger<MetadataExtractor>());

Console.WriteLine($"Emitted {result.PagesEmitted} pages across {result.CanonicalTypes} types.");
```

### Streaming pages instead of writing to disk

`CallbackPageSink` invokes a `(string relativePath, byte[] utf8Bytes)`
callback per page — useful when piping the output into another pipeline
(e.g. an in-memory render queue) without staging anything on disk:

```csharp
var sink = new CallbackPageSink((relativePath, bytes) =>
{
    Console.WriteLine($"{relativePath}: {bytes.Length} bytes");
});

await new MetadataExtractor().RunAsync(source, sink, emitter, logger);
```

The bytes are encoded once into a freshly-allocated `byte[]` so the callback
owns them outright — safe to retain, no array-pool ties.

### Package roots and references

`NuGetAssemblySource` uses NuGet.Client PackageReference restore. Dependency
ranges, framework groups, source mappings, transitive conflicts, and compile
assets come from NuGet's resolved graph. Each root and selected TFM has its
own `project.assets.json` under `apiPath/restore/`; packages use the normal
NuGet global cache. Restore inputs, pins, runtime identifiers, and effective
NuGet configuration distinguish graph directories.

Only assemblies supplied by declared documentation roots produce pages and
local API links. Transitive packages provide metadata for signatures and type
resolution. Forwarded types are documented when their defining package is
selected. `excludePackages` and `excludePackagePrefixes` exclude documentation
roots, while required dependencies remain available as references. Exclusions
match package IDs: `Reactive.Wasm`, for example, ships `System.Reactive.Wasm.dll`.

For repeatable inputs, specify an optional root version:

```json
{
  "additionalPackages": [{ "id": "Refit", "version": "15.2.0" }],
  "tfmPreference": ["net10.0"],
  "tfmOverrides": { "Refit": "net10.0" }
}
```

An omitted version selects the latest stable root. An explicit version takes
precedence over owner discovery. `dependencyPins` is an optional object mapping
package IDs to exact versions or NuGet ranges; those constraints participate as
direct references in each graph. `runtimeIdentifier` selects an optional RID.
Use `referencePackages` for explicit references and targeting packs, scoped by
`targetTfm`.

Package documentation runs through NuGet APIs without MSBuild, an installed
SDK, or installed workloads. Framework reference DLLs come from NuGet reference
packs. Android and Apple pack identities and versions come from manifest
packages downloaded through the configured feeds. Reading Apple reference DLLs
works on any supported host operating system.

Only compatible, required reference DLLs from the resolved package graph are
added to the parser. Package build targets are not executed. Use an explicit
`referencePackages` entry with `pathPrefix` for prebuilt assemblies in a
nonstandard package directory.

Restore failures report the root, TFM, RID, NuGet diagnostic code, and dependency
chain. Missing assembly diagnostics identify the documented API member requiring
the reference and its root assembly. Private implementation references and unused
dependency type forwarders do not produce documentation warnings. Reference
completion inspects type metadata without loading XML documentation; page
generation loads the requested XML documentation.
Missing published manifest declarations identify the reference pack that needs
an explicit version in `referencePackages`.

---

## Supported target frameworks

Package metadata and available reference packs determine target-framework
support independently of the host SDK and workload lifecycle:

- **Modern .NET (5.0+)** — `net5.0`, `net6.0`, `net7.0`, `net8.0`, `net9.0`,
  `net10.0`, `net11.0`, plus the `net*-android`, `net*-ios`, `net*-maccatalyst`,
  `net*-windows` workload variants. See the official
  [.NET and .NET Core support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
- **netstandard** — `netstandard1.0` through `netstandard2.1`. Sticks around
  because the BCL targets it, even though
  [no future netstandard releases are planned](https://learn.microsoft.com/en-us/dotnet/standard/net-standard).
- **.NET Framework, net462 and newer** — `net462`, `net47`, `net471`,
  `net472`, `net48`, `net481`. Reference assemblies are available as NuGet
  packages. See the
  [.NET Framework support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework)
  for which of those are still in mainstream / extended support.

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

Packages that ship **only** legacy TFMs are skipped at fetch time. Skips are
logged at **information** level so they don't drown out genuine warnings on
real-world walks (ReactiveUI / Avalonia / Splat surfaces typically pull
several dozen System.* packages whose entire TFM list is legacy-only). Set
your logger filter to `Information` if you want to see the legacy-skip list
during a build.

---

## Performance

### Comparison with Docfx 2.80.1

For Refit 15.2.0 on `net10.0`, API extraction and YAML writing measured:

| Generator | Mean ± 99.9% confidence interval | Managed allocation |
|---|---:|---:|
| SourceDocParser with its Docfx YAML emitter | 123.9 ± 6.70 ms | 128.22 MiB |
| Docfx 2.80.1 | 564.1 ± 7.58 ms | 501.37 MiB |

That is about **4.6× faster and 74% less allocation for this workload**. Both
outputs parse as YAML and describe the same 490 API entries across 83 types.
The comparison uses identical assembly inputs and matched visibility settings;
it excludes package fetching and site rendering. Docfx does more out of the box,
and these measurements cover only the API-generation stage shared by both tools.
See the [benchmark notes](src/benchmarks/README.md) for methodology, output
differences, and how the tools complement different documentation workflows.

### Package fetching

The fetch comparison pins Refit 15.2.0 to `net10.0` and
Microsoft.NETCore.App.Ref to 10.0.0, runs on .NET 11 in Release mode, and retains
downloaded package caches. Fresh-source runs regenerate graph output. Every
operation verifies one documentation root; selected reference entries are 314
for 2.2.0 and 171 for the NuGet restore pipeline. Cached and fresh discovery
return identical graph and assembly hashes within each implementation.

| Scenario | Published 2.2.0 | NuGet restore pipeline | Managed allocations (2.2.0 → restore) |
|---|---:|---:|---:|
| Warm source and graph | 2,088 ± 8.4 ms | 31.56 ± 2.616 ms | 83.86 → 13.80 MB |
| Fresh source and graph | 4,978 ± 35.5 ms | 25.83 ± 1.663 ms | 89.96 → 14.01 MB |

Intervals are BenchmarkDotNet's 99.9% confidence intervals from five warmups and
15 measurements on a Ryzen 7 5800X, pinned to seven physical cores with the
performance governor. Allocation counts cover the benchmark process. Allocation
profiling runs separately from timing. The repository harness uses automatic
framework-pack selection; the comparison adds the same reference-pack pin to
both implementations.

```bash
cd src
dotnet run --project benchmarks/SourceDocParser.Benchmarks --framework net11.0 \
  --configuration Release -- --filter '*NuGetFetchBenchmarks*'
```

### Pipeline workload

**Benchmark workload.** Numbers below are from the BenchmarkDotNet suite
under `src/benchmarks/SourceDocParser.Benchmarks/`, run on a Ryzen 7 5800X /
.NET 10. The workload extracts three NuGet packages from `nuget.org`
— pulling each package's `lib/` and `ref/` trees and the matching reference
assemblies, walking every public symbol across ~19 target-framework groups,
parsing the shipped XML doc files, resolving `<inheritdoc/>` chains, and
emitting roughly 600 canonical type pages after cross-TFM merge. The local
NuGet cache is warmed once during global setup so per-iteration timings
measure the walk + merge + emit pipeline, not the network leg.

**End-to-end (`MetadataExtractor.RunAsync`):**

| Phase                                | Wall time | Allocated |
|--------------------------------------|----------:|----------:|
| Full pipeline (`RunAsync`)           |   ~1.5 s  |  ~525 MB  |
| Discover (NuGet config + cache scan) |  ~990 ms  |  ~258 MB  |
| Load + walk (parallel, all groups)   |  ~509 ms  |  ~236 MB  |
| Merge (cross-TFM dedup)              |   ~1 ms   |  ~380 KB  |
| Emit (Zensical Markdown)             |  ~139 ms  |   ~39 MB  |

The walk phase walks one Roslyn compilation per package — one canonical TFM
per equivalence class. Other TFMs whose public-API surface is a subset of
the canonical's are folded in via a `MetadataReader` probe that only
enumerates type tokens, no symbol tree, no constructed types. The merger
then broadcasts the canonical's walked types into each subset TFM so
`ApiType.AppliesTo` still records every TFM the type applies to.

**Per-call hotspots:**

| Operation                                                                |    Time | Allocated |
|--------------------------------------------------------------------------|--------:|----------:|
| `XmlDocToMarkdown.Convert` — plain summary                               |  ~24 ns |     176 B |
| `XmlDocToMarkdown.Convert` — tagged with `<see>` / `<c>` / `<paramref>`  | ~916 ns |     456 B |
| `XmlDocToMarkdown.Convert` — code block + bullet list                    | ~1.2 µs |     440 B |
| `TfmResolver.FindBestRefsTfm` — exact match                              |   ~3 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` — platform-suffix strip                    |  ~11 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` — netstandard fallback                     | ~496 ns |     1 KB  |
| `TypeMerger.Merge` — 600 types × 3 TFMs                                  | ~115 µs |    358 KB |

**Our emitter cost per type page** (no I/O, just markup formatting; baseline =
our Zensical Markdown emitter). Both columns use SourceDocParser; this table
does not measure the Docfx application:

| Workload (types × members/type) | Our Zensical Markdown emitter | Our Docfx YAML emitter | Time | Alloc |
|---------------------------------|--------------------:|----------------------:|------:|------:|
| 100 × 5                         |   72 µs / 288 KB    |   618 µs / 1,366 KB   |  8.6× |  4.7× |
| 100 × 30                        |  263 µs / 763 KB    | 5,432 µs / 6,338 KB   | 20.7× |  8.3× |
| 600 × 5                         |  437 µs / 1,730 KB  | 3,605 µs / 8,198 KB   |  8.3× |  4.7× |
| 600 × 30                        | 1,505 µs / 4,580 KB | 17,122 µs / 38,025 KB | 11.4× |  8.3× |

ManagedReference YAML carries explicit member metadata and cross-reference
records used by Docfx's documentation pipeline. Our YAML emitter serializes
those additional fields; our Markdown emitter produces the page content chosen
for MkDocs / Zensical. The formats serve different consumers, so their formatting
costs are not a measure of equivalent site-building capabilities.

### How perf and allocations stay low

- **MetadataReader probe + canonical-only Roslyn walk.** The walker only spins up one Roslyn compilation per package — the canonical TFM picked by descending rank. Other TFMs whose public type set is a subset of the canonical's are detected via a `System.Reflection.Metadata.MetadataReader` probe (no symbol binding, no constructed-type allocation) and folded into `ApiType.AppliesTo` via a synthetic broadcast catalog that reuses the canonical's already-walked types. TFMs whose surface is *not* a subset still get a full Roslyn walk so removed-in-newer-TFM types stay in the catalog.
- **Custom span-based XML scanner.** A `ref struct DocXmlScanner` walks `///` doc fragments directly over `ReadOnlySpan<char>`, implementing just the XML grammar doc comments use. `XmlReader`'s `XmlTextReaderImpl` allocates multi-KB internal buffers (`NodeData[]`, `NamespaceManager`, char buffers) per construction; the scanner avoids that. Both the per-symbol parser and the Markdown renderer drive it, so per-element XML processing is allocation-free apart from the result string.
- **Build-once-then-read-many `XmlDocSource`.** Each `.xml` doc file is read once via `File.ReadAllBytes` + `Encoding.UTF8.GetString` and indexed by per-member `(offset, length)` ranges; substrings materialise only when a consumer calls `Get(memberId)`. Safe for concurrent reads from the parallel walker.
- **Eager per-group loader disposal.** Each TFM group's `CompilationLoader` holds memory-mapped views of every reference DLL. An interlocked counter retires the loader as soon as its last assembly finishes; peak working set scales with the slowest-finishing group, not the total number of groups times their references.
- **Streaming type merger.** The parallel walk feeds `ApiCatalog`s into `StreamingTypeMerger` one at a time and immediately drops the reference. Catalogs don't accumulate in a `ConcurrentBag` waiting for the walk phase to finish.
- **Streaming page sink.** `IPageSink` lets the emitter hand each page off as bytes the moment it's rendered — `FilePageSink` flushes through `PageWriter` (chunked UTF-8 via `ArrayPool<byte>` into an unbuffered `FileStream`); `CallbackPageSink` invokes a delegate so callers can pipe into a `Channel`, an HTTP body, or another in-process pipeline without ever staging files on disk.
- **Capture-free parallel dispatch.** The `Parallel.ForEachAsync` lambda is `static`; every dependency it touches is bundled into a `WalkContext` record attached to each work item, so dispatch never allocates a closure object per assembly.
- **Lazy `RenderedDoc` facade for emit-time conversion.** Walker output carries raw inner-XML fragments. Each emitter constructs an `XmlDocToMarkdown(ICrefResolver)` and wraps each symbol's documentation in a `RenderedDoc` that converts each text-shaped field on first read, caches the result, and skips fields the page doesn't consume. Zensical and docfx pick their own cref form (`[name][uid]` autoref vs `<xref:uid>` / Microsoft Learn URL) without the walker baking either in.
- **Thread-static `PageBuilderPool`.** Each emit thread reuses one `StringBuilder` across page composition calls via a `using`-scoped rental; pages clear the builder between uses instead of allocating fresh.
- **Shared `CatalogIndexes` rollup.** Derived-class lookup, reverse extension-method lookup, and per-type inherited-member uid lists are built once per emit run in a single O(N) sweep and frozen via `FrozenDictionary`. Each emitter passes its own `System.Object` baseline UIDs (docfx bare names, Zensical `M:`-prefixed commentIds) so the algorithm stays shared while the wire format stays per-emitter.
- **Pre-sized buffers and stackalloc paths.** nupkg zip entries size their backing `byte[]` to the known uncompressed length up front. SourceLink URL rewriting and `ZensicalCrefResolver`'s Microsoft Learn link composer build their result strings via `stackalloc` + `new string(span)` so the only heap allocation is the returned string itself.

---

## Repository layout

```
SourceDocParserLib/
  icons/SourceDocParserIcon.png        package icon (packed into every nupkg)
  src/
    SourceDocParser/                   core walker / merger / sinks
    SourceDocParser.Common/            shared primitives
    SourceDocParser.NuGet/             nuget.org IAssemblySource
    SourceDocParser.Docfx/             docfx YAML emitter
    SourceDocParser.Zensical/          mkdocs-Material Markdown emitter
    benchmarks/                        BenchmarkDotNet harness
    tests/
      SourceDocParser.Tests/             unit tests (TUnit)
      SourceDocParser.NuGet.Tests/
      SourceDocParser.Zensical.Tests/
      SourceDocParser.Docfx.Tests/
      SourceDocParser.IntegrationTests/  end-to-end + Zensical render-smoke
    Directory.Build.props              shared lib config (MinVer, packing, analyzers)
    Directory.Packages.props           central package versions
    SourceDocParserLib.slnx
  .editorconfig
  stylecop.json
```

`dotnet build` from `src/` packs every non-test project into
`artifacts/packages/` automatically (`<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`).
Consumers in other repos can wire that directory up as a local feed via
`nuget.config` until the libraries are published.

### Versioning

`MinVer` derives the version from the most recent `v*` git tag. Untagged
commits build as `{nextMinor}.0.0-alpha.0.{height}+sha` (auto-increment
minor). Releases run via the `Release` workflow (`workflow_dispatch`) — pick
`major` / `minor` / `patch`; the workflow computes the next version from the
latest RTM tag in shell, creates the `v$VERSION` tag, propagates the version
via `MINVERVERSIONOVERRIDE` so every downstream MSBuild project skips
MinVer's per-project git walk, then builds / packs / signs / pushes to
NuGet and creates the GitHub Release.

---

## Acknowledgements

The metadata extraction pipeline is inspired by — and lifts patterns from
— [dotnet/docfx](https://github.com/dotnet/docfx) (MIT licensed). docfx's
Roslyn-based assembly walker, inheritdoc resolution, and overall metadata
model shaped this library's design. See [`LICENSE`](./LICENSE) for the
original docfx attribution.

Built on:

- [Roslyn](https://github.com/dotnet/roslyn) (Microsoft.CodeAnalysis.CSharp) for compilation + symbol model
- [ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy) for transitive reference resolution
- [NuGet.Frameworks](https://github.com/NuGet/NuGet.Client) + [NuGet.Versioning](https://github.com/NuGet/NuGet.Client) for proper TFM compatibility and SemVer ordering
- .NET rate limiting and exponential backoff for HTTP requests

## License

MIT — see [`LICENSE`](./LICENSE) for the full text and the docfx attribution.

[Core]: https://www.nuget.org/packages/SourceDocParser/
[CoreV]: https://img.shields.io/nuget/v/SourceDocParser.svg?logo=nuget&label=
[Common]: https://www.nuget.org/packages/SourceDocParser.Common/
[CommonV]: https://img.shields.io/nuget/v/SourceDocParser.Common.svg?logo=nuget&label=
[Pkg]: https://www.nuget.org/packages/SourceDocParser.NuGet/
[PkgV]: https://img.shields.io/nuget/v/SourceDocParser.NuGet.svg?logo=nuget&label=
[Zen]: https://www.nuget.org/packages/SourceDocParser.Zensical/
[ZenV]: https://img.shields.io/nuget/v/SourceDocParser.Zensical.svg?logo=nuget&label=
[Docfx]: https://www.nuget.org/packages/SourceDocParser.Docfx/
[DocfxV]: https://img.shields.io/nuget/v/SourceDocParser.Docfx.svg?logo=nuget&label=
