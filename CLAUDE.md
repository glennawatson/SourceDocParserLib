# CLAUDE.md

## Repository Orientation

- **Primary working directory for build/test:** `./src`
- **Main solution:** `src/SourceDocParserLib.slnx`
- **Production libraries:**
  - `src/SourceDocParser/` — Core walker, merger, source-link resolution. Defines the `IAssemblySource`, `IDocumentationEmitter`, `IMetadataExtractor` seams.
  - `src/SourceDocParser.NuGet/` — `IAssemblySource` that fetches packages from `nuget.org` by owner / explicit list and exposes the per-TFM `lib/` trees. Owns `INuGetFetcher` + the streaming `JsonDocument`-based `PackageConfigReader`.
  - `src/SourceDocParser.Zensical/` — `IDocumentationEmitter` that writes Markdown tuned for Zensical / mkdocs Material (admonitions, content tabs, mermaid).
  - `src/SourceDocParser.Docfx/` — docfx config-file shim. Reads + writes `docfx.json` shapes via `DocfxConfigReader` / `DocfxConfigWriter` (hand-written `JsonDocument` reader, `Utf8JsonWriter` writer; no source-gen serializer in the path). No emitter — that ships separately.
- **Test projects:**
  - `src/tests/SourceDocParser.Tests/` — Pure unit tests (TUnit). Covers `TfmResolver`, `TypeMerger`, `XmlDocToMarkdown`, `DocfxConfigReader` round-trip, and `MetadataExtractor` argument validation through fake `IAssemblySource` / recording `IDocumentationEmitter` impls. Uses `Fixtures/nuget-packages.json` (slim debug config) and shares it with the integration tests via a `Link` reference.
  - `src/tests/SourceDocParser.IntegrationTests/` — End-to-end pipeline test that fetches the slim fixture from nuget.org, walks it through the parser, and asserts catalog/page output. Plus `ZensicalRenderSmokeTests` which bootstraps a Python venv under `zensical/.venv` and runs `zensical build --strict` against the bundled mock site fixture.

## Build

```bash
# Restore + build (from src/)
cd src
dotnet build SourceDocParserLib.slnx

# Build a single project
dotnet build SourceDocParser/SourceDocParser.csproj
```

`<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` is set in `src/Directory.Build.props` for every non-test project, so each `dotnet build` also writes `.nupkg` + `.snupkg` to `artifacts/packages/`. Consumers in other repos (e.g. the `reactiveui/website` Nuke build) wire that directory up as a local feed via `nuget.config` until the libraries are published to nuget.org.

## Testing: Microsoft Testing Platform (MTP) + TUnit

This repo uses **Microsoft Testing Platform (MTP)** with **TUnit** (not VSTest).

- MTP is configured via `src/global.json` (`"runner": "Microsoft.Testing.Platform"`).
- `TestingPlatformDotnetTestSupport` is enabled in `src/Directory.Build.props`.
- `IsTestProject` is auto-detected via `$(MSBuildProjectName.Contains('Tests'))` in `Directory.Build.props`. Test projects automatically get `<OutputType>Exe</OutputType>`, the TUnit + Verify.TUnit packages, the implicit-usings switches, and `<NoWarn>$(NoWarn);CA1812</NoWarn>`.
- `IsPackable=false` is set on each test project explicitly (defence-in-depth — Directory.Build.props sets it too).

### Test Commands (run from `./src`)

**CRITICAL:** Run `dotnet test` from the `src/` directory so the `global.json` MTP runner config is discovered. Use `--project` to specify the test project.

```bash
cd src

# Unit tests
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj

# Integration tests (will hit nuget.org for the slim fixture, ~10s on first run)
dotnet test --project tests/SourceDocParser.IntegrationTests/SourceDocParser.IntegrationTests.csproj

# Detailed output (place BEFORE --)
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --output Detailed

# List tests
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --list-tests

# Fail fast
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --fail-fast

# Run a single test method by tree-node filter
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --treenode-filter "/*/*/TfmResolverTests/FindBestRefsTfmHandlesPlatformSuffix"

# All tests in a class
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --treenode-filter "/*/*/TfmResolverTests/*"
```

### Testing Best Practices

- **Do NOT use `--no-build`** — always build before testing to avoid stale binaries.
- Use `--output Detailed` **before** `--` for verbose output.
- The integration tests touch the network on first run to populate the local NuGet cache; subsequent runs reuse `apiPath/cache/*.nupkg` and complete in ~3 seconds.

### Code Coverage

Coverage uses **Microsoft.Testing.Extensions.CodeCoverage** wired in via `src/Directory.Build.props` (added to every test project). Per-assembly options (format, attribute exclusions) live in `src/testconfig.json` and are linked next to each test binary as `<AssemblyName>.testconfig.json`.

```bash
cd src

# Run unit tests with coverage
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --coverage --coverage-output-format cobertura

# Generate an HTML report (install once: dotnet tool install -g dotnet-reportgenerator-globaltool)
reportgenerator \
  -reports:"**/TestResults/**/*.cobertura.xml" \
  -targetdir:/tmp/sourcedocparser_coverage \
  -reporttypes:"Html;TextSummary"
cat /tmp/sourcedocparser_coverage/Summary.txt
```

### Benchmarks

`src/benchmarks/SourceDocParser.Benchmarks/` is a BenchmarkDotNet harness covering `MetadataExtractor.RunAsync` end-to-end against the slim debug NuGet fixture (3 owner-discovered packages, 19 TFM groups). The global setup runs one full fetch to warm the local NuGet cache, so per-iteration timings measure the walk + merge + emit pipeline without the network leg.

```bash
cd src

# Run every benchmark in the assembly
dotnet run --project benchmarks/SourceDocParser.Benchmarks/SourceDocParser.Benchmarks.csproj --configuration Release

# Filter to a single benchmark via the BenchmarkDotNet switcher
dotnet run --project benchmarks/SourceDocParser.Benchmarks/SourceDocParser.Benchmarks.csproj --configuration Release -- --filter '*RunAsync*'
```

### Zensical render-smoke

`ZensicalRenderSmokeTests` runs the actual `zensical build --strict` against a bundled mock site (`tests/SourceDocParser.IntegrationTests/zensical/mock-site/`) so we catch Zensical/Material rendering bugs that pure-C# tests can't. Self-bootstrapping:

1. On first run the test creates a Python virtualenv at `tests/SourceDocParser.IntegrationTests/zensical/.venv` via `python3 -m venv` (assumes `python3` is on PATH; gracefully skips otherwise).
2. Installs `requirements.txt` (`zensical`, `pymdown-extensions`) into the venv.
3. Writes a `.requirements-installed` marker; subsequent runs skip the install when the marker is newer than `requirements.txt`.
4. Runs `zensical build --strict` from the venv against the mock site.
5. Asserts a zero exit code; stdout/stderr surface in the failure message.

The venv lives under the project so it's isolated from the user's system Python and gitignored (see `.gitignore` — `.venv/` excluded along with `site/`, `__pycache__/`, etc.).

## Code Style

- `.editorconfig` at the repo root drives formatting + IDExxxx severities.
- StyleCop, Roslynator, and Blazor.Common analyzers are active in every project (configured in `src/Directory.Build.props`).
- `EnforceCodeStyleInBuild=true` so editorconfig severities for IDExxxx rules fire at compile time.
- File header copyright text comes from `stylecop.json` (`"companyName": "Glenn Watson and Contributors"`); SA1636 enforces every `.cs` file matches.
- Public APIs require XML documentation (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`); SA1600 / SA1611 / SA1615 catch missing element / parameter / return docs.
- Logging is source-generated `[LoggerMessage]` partial methods on `ILogger` parameters — no `logger.LogInformation("...")` direct calls (CA1848). Expensive argument expressions go behind `LogInvokerHelper.Invoke(...)` to gate evaluation on `IsEnabled`.
- Public concrete classes that have an interface counterpart (`MetadataExtractor`/`IMetadataExtractor`, `NuGetFetcher`/`INuGetFetcher`, `SourceLinkValidator`/`ISourceLinkValidator`) keep their private helpers + `[LoggerMessage]` partials `static`; only the public entry point is instance.
- Path-typed public APIs use `string`, never `Nuke.Common.IO.AbsolutePath` — the libraries don't take a Nuke dependency. Validate with `ArgumentException.ThrowIfNullOrWhiteSpace`.

## Versioning

`Nerdbank.GitVersioning` (`version.json` at the repo root) computes version on every build. Base is `0.1-alpha`. Public releases are gated on the `master`/`main` branch via `publicReleaseRefSpec`; off-branch builds get the height + commit suffix.

## Acknowledgements

The metadata extraction pipeline is inspired by — and lifts patterns from — [dotnet/docfx](https://github.com/dotnet/docfx) (MIT). See `LICENSE` for the original docfx attribution.
