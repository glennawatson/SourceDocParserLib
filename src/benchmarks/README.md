# SourceDocParser and Docfx

SourceDocParser focuses on compiled .NET APIs, with opinionated Markdown output
for MkDocs / Zensical. Its catalog and page-sink interfaces let another system
own the website. The Docfx emitter maps that catalog to ManagedReference YAML;
it does not reproduce every Docfx output detail or policy.

[Docfx](https://dotnet.github.io/docfx/) provides a broader documentation
toolchain out of the box: source/project and assembly API extraction, conceptual
Markdown, REST API documentation, templates, navigation, cross-references, and
HTML site building and serving. Its richer models and output support those
workflows. SourceDocParser also draws on Docfx's MIT-licensed metadata-extraction
work; see the repository [acknowledgements](../../README.md#acknowledgements).

| Area | SourceDocParser | Docfx |
|---|---|---|
| Primary role | Embeddable API catalog, merger, and emitters for a host documentation pipeline | Integrated API and conceptual documentation site toolchain |
| Typical workflow | Published NuGet packages or compiled assemblies → API pages for MkDocs / Zensical | Source projects, assemblies, and documentation content → a Docfx site |
| Page decisions | Opinionated page structure, cross-TFM merging, and links within declared documentation roots | Docfx metadata models, filtering, templates, navigation, and cross-reference processing |
| Docfx integration | A ManagedReference YAML adapter over our catalog | Native metadata extraction and site rendering |

Choose according to the documentation workflow and required features. An API
extraction benchmark does not measure a complete website build or establish
feature parity.

## Pinned Refit comparison

Measured on September 19, 2026: SourceDocParser commit
[`6f4b303`](https://github.com/glennawatson/SourceDocParserLib/commit/6f4b3036c2489bb749e5040c56e8dfb6f30039f1)
and Docfx 2.80.1, extracting metadata and writing ManagedReference YAML files.

| Generator | Mean ± 99.9% confidence interval | Managed allocation per operation |
|---|---:|---:|
| SourceDocParser with `DocfxYamlEmitter` | 123.9 ± 6.70 ms | 128.22 MiB |
| Docfx 2.80.1 | 564.1 ± 7.58 ms | 501.37 MiB |

SourceDocParser was **4.55× faster and allocated 74.4% less managed memory for
this workload**. These figures are not a general speed ratio for the products.

### Inputs and measurement

- Refit **15.2.0**, selected TFM **net10.0**, and
  Microsoft.NETCore.App.Ref **10.0.0**.
- One documentation root and the same **171 SHA256-verified assemblies**,
  including the root, with their adjacent XML documentation. Reference DLLs were
  staged beside the root to give both assembly resolvers a coherent input set.
- Docfx's `disableDefaultFilter` was enabled to match SourceDocParser's public
  surface. Its default filtering hides some public implementation-oriented APIs;
  changing that policy changes the workload.
- Package acquisition and graph resolution completed before timing. Each timed
  operation performed metadata extraction and YAML writing to fresh output.
  HTML rendering, templates, search, and site serving were excluded.
- BenchmarkDotNet **0.15.8**, .NET **10.0.12**, Release configuration,
  in-process jobs, five warmups, 15 measurements, one invocation per iteration.
  The Ryzen 7 5800X host used seven physical cores, the performance governor,
  and process priority -20. No profiler was attached to timing runs.
- The Docfx comparison used Roslyn **5.9.0** and its required binary-compatible
  Decompiler **9.1.0.7988** and YamlDotNet **16.3.0** dependencies.

### Output equivalence and differences

Both outputs parsed successfully and described the same **490 API identifiers
across 83 types**. Enum values were counted from each emitter's representation:
our emitter places them in the enum's syntax parameters; Docfx emits field items.
SourceDocParser wrote **84 YAML files**; Docfx wrote **85**, including an extra
table-of-contents file.

Matching API inventories establishes that both runs included the same
declarations. It does not establish identical markup, reference expansion,
filtering defaults, or rendered websites. Docfx's output was about 3.7 MiB;
ours was about 0.59 MiB. Those differences reflect the work and output choices
being measured, and are part of the tradeoff rather than a claim that one
format's additional metadata is unnecessary.

Separate GcVerbose runs attributed about **91% of SourceDocParser's sampled
allocation** to XML documentation loading and indexing. Docfx's largest sites
included symbol filtering, XML processing, YAML traversal, and reference URL
generation. Profiled timings were excluded from the table above.

## Repository harnesses

- `SourceDocParser.Benchmarks`: parser and emitter benchmarks, including pinned
  Refit package-fetch tests.
- `SourceDocParser.Docfx.StandaloneBenchmarks`: our metadata-to-YAML pipeline.
- `Docfx.StandaloneBenchmarks`: Docfx's metadata-to-YAML pipeline.

Run the projects from `src/`, following the commands in
[`CLAUDE.md`](../../CLAUDE.md#benchmarks). The standalone runners' default
multi-package fixture differs from the pinned Refit setup above. To compare a
different workload, align root versions, TFMs, reference files, visibility
settings, and emitted API inventories before interpreting timings.
