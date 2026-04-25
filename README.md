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
