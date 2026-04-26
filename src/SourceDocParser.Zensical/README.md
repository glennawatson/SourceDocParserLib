# SourceDocParser.Zensical

API documentation emitter that produces mkdocs-Material / Zensical-flavoured Markdown from a `SourceDocParser` walk. One file per type, plus one file per overload group on the type.

The output is plain mkdocs-Material Markdown. It works under either the `mkdocs` CLI with `mkdocs-material` installed, or under the `zensical` CLI — the page shape is identical. "Zensical" in the package name reflects the target site convention; it does not require Zensical at runtime.

## What it emits

For each walked `ApiType`:

- `<package>/<Namespace>/<TypeName>.md` — the type page (summary, hierarchy mermaid diagram, applies-to, members table, enum values / delegate signature where applicable).
- `<package>/<Namespace>/<TypeName>/<MemberName>.md` — one page per overload group (constructor / property / method / event / field / operator).

The leading `<package>/` segment only appears when you configure `PackageRoutingRule`s (see below). Without routing, layout is a flat namespace tree.

## Cross-link routing

Type references (base types, interfaces, parameter types, return types, exception types, see-also entries) are routed through `CrossLinkRouter`:

| Reference shape | Output |
| --- | --- |
| BCL type (`System.*`, `Microsoft.*`) | `[Foo](https://learn.microsoft.com/dotnet/api/system.foo)` Microsoft Learn URL — slug is the lower-cased full name with `` ` `` replaced by `-`. |
| Anything else with a UID | `[Foo][T:Bar.Foo]` mkdocs-autorefs link, resolved by the `autorefs` plugin against the type page heading anchor. |
| No UID | `` `Foo` `` — inline code fallback. |

The Microsoft Learn base URL is configurable via `ZensicalEmitterOptions.MicrosoftLearnBaseUrl` (default: `https://learn.microsoft.com/dotnet/api/`).

The walker emits constructed-generic UIDs (`T:System.Action{`0}`); `UidNormaliser` rewrites these to the canonical open-generic form (`T:System.Action`1`) at link time so autorefs and Microsoft Learn URLs both line up.

## Per-package output routing

```csharp
var options = new ZensicalEmitterOptions(
    PackageRouting:
    [
        new PackageRoutingRule(FolderName: "ReactiveUI",         AssemblyPrefix: "ReactiveUI"),
        new PackageRoutingRule(FolderName: "ReactiveUI.Wpf",     AssemblyPrefix: "ReactiveUI.Wpf"),
        new PackageRoutingRule(FolderName: "Splat",              AssemblyPrefix: "Splat"),
    ]);

var emitter = new ZensicalDocumentationEmitter(options);
await emitter.EmitAsync(types, outputRoot: "docs/api");
```

Match is **first-rule-wins**, prefix-with-dot semantics: `AssemblyPrefix: "ReactiveUI"` matches assembly `ReactiveUI` and `ReactiveUI.Foo`, but not `ReactiveUI.Wpf` if a more specific rule comes first. Order more specific prefixes earlier.

When any rules are configured, types from non-matching assemblies are skipped from the walk — they are referenced via cross-links rather than getting their own pages. Use this to keep transient package types out of your site without needing per-page filtering.

Without rules (`ZensicalEmitterOptions.Default`), every walked type produces a page in the legacy flat namespace layout.

## Required mkdocs-Material extensions

The emitter assumes the consuming site has these `markdown_extensions:` enabled. They are also valid Zensical extensions.

```yaml
markdown_extensions:
  - attr_list
  - admonition
  - md_in_html
  - tables
  - def_list
  - footnotes
  - toc:
      permalink: true
  - pymdownx.details
  - pymdownx.superfences:
      custom_fences:
        - name: mermaid
          class: mermaid
          format: !!python/name:pymdownx.superfences.fence_code_format
  - pymdownx.tabbed:
      alternate_style: true
  - pymdownx.highlight:
      anchor_linenums: true
      line_spans: __span
      pygments_lang_class: true
  - pymdownx.inlinehilite
  - pymdownx.snippets
  - pymdownx.emoji
  - pymdownx.tasklist
  - pymdownx.keys
```

Required plugins:

```yaml
plugins:
  - search
  - autorefs   # resolves [Foo][T:Bar.Foo] cross-links
```

## Sample configs

See `samples/`:

- `samples/zensical.toml` — minimal Zensical site config consuming the emitter output.
- `samples/mkdocs.yml` — equivalent mkdocs-Material site config.

Both consume the `docs/api/` tree the emitter writes. Hand-author top-level navigation; let `mkdocs-awesome-pages-plugin` (mkdocs) or directory-based discovery (Zensical) absorb the per-namespace API tree.
