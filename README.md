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
See the [performance overview and comparison](docs/performance.md) for the
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

SourceDocParser focuses on fast API generation, with package, framework, and
output choices determining the work required. See the
[performance overview](docs/performance.md) for measurements, tradeoffs, and
comparisons.

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
