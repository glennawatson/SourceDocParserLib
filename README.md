# SourceDocParserLib

Roslyn-based .NET assembly walker that turns compiled `.dll` + `.pdb` + `.xml` triples into a strongly-typed API catalog (types, members, signatures, XML docs, inheritdoc, SourceLink) and hands it to a pluggable emitter for rendering.

The catalog is **format-neutral**. Emitters decide how to render it — Markdown for Zensical / mkdocs Material today, with room for other targets.

## Packages

| Package | What it does |
|---|---|
| `SourceDocParser` | Core walker, merger, source-link resolution. Defines `IAssemblySource`, `IDocumentationEmitter`, `IMetadataExtractor`. |
| `SourceDocParser.NuGet` | `IAssemblySource` that fetches packages from `nuget.org` by owner / explicit list and exposes the per-TFM `lib/` trees. |
| `SourceDocParser.Zensical` | `IDocumentationEmitter` that writes Markdown tuned for Zensical / mkdocs Material (admonitions, content tabs, mermaid). |
| `SourceDocParser.Docfx` | docfx config-file shim — reads + writes `docfx.json` shapes so an existing docfx site can drive the parser pipeline. No emitter; that ships separately. |

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
| `XmlDocToMarkdown.Convert` — plain summary                               |  ~26 ns |     176 B |
| `XmlDocToMarkdown.Convert` — tagged with `<see>` / `<c>` / `<paramref>`  | ~890 ns |     304 B |
| `XmlDocToMarkdown.Convert` — code block + bullet list                    | ~1.1 µs |     440 B |
| `TfmResolver.FindBestRefsTfm` — exact match                              |   ~3 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` — platform-suffix strip                    |  ~11 ns |       0 B |
| `TfmResolver.FindBestRefsTfm` — netstandard fallback                     | ~500 ns |     1 KB  |
| `TypeMerger.Merge` — 600 types × 3 TFMs                                  | ~120 µs |    330 KB |

### Strategies the pipeline uses

- **Custom span-based XML scanner.** Every NuGet package ships an `<assembly-name>.xml` doc file alongside its `.dll`, holding the `///` doc comments for every public symbol. The walker has to read each member's XML fragment per symbol, render its `<see>` / `<c>` / `<list>` / `<inheritdoc>` tags into Markdown, and do the same again per `<param>` / `<exception>` inside it — for thousands of symbols per assembly. `XmlReader` works for that, but its `XmlTextReaderImpl` allocates multi-KB internal buffers (`NodeData[]`, `NamespaceManager`, char buffers, `Entry[]`) per construction, which dominates the doc-parse profile. So the pipeline ships a small `ref struct DocXmlScanner` that walks the doc text directly over `ReadOnlySpan<char>` and implements just the XML grammar that `///` doc comments actually use. Both the per-symbol parser and the Markdown renderer drive the scanner, so per-element XML processing is allocation-free apart from the result string.
- **Build-once-then-read-many `XmlDocSource`.** Each `.xml` doc file is read once via `File.ReadAllBytes` + `Encoding.UTF8.GetString`, then indexed by per-member `(offset, length)` ranges. The substring is only materialised when a consumer calls `Get(memberId)`, and the source is safe for concurrent reads under the parallel walker.
- **Eager per-group loader disposal.** Each TFM group has its own `CompilationLoader` with a private `MetadataReferenceCache` holding memory-mapped views of every reference DLL. As soon as the last assembly in a group finishes its walk, an interlocked counter drops to zero and the loader disposes — peak working set scales with the slowest-finishing group, not the total number of groups times their references.
- **Streaming type merger.** The parallel walk feeds `ApiCatalog`s into `StreamingTypeMerger` one at a time and immediately drops its reference, instead of accumulating every catalog in a `ConcurrentBag` until the walk phase finishes.
- **Capture-free parallel dispatch.** The `Parallel.ForEachAsync` lambda is `static` — every dependency it touches is bundled into a `WalkContext` record attached to each work item, so dispatch never allocates a closure object per assembly.
- **Pooled `StringBuilder` on the converter.** `XmlDocToMarkdown` is per-walk by construction; reusing a single builder across every `Convert` call eliminates the per-element allocation that would otherwise dominate the renderer.
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
