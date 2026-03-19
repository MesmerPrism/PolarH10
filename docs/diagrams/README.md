# Diagram Viewer

Interactive browser-based viewer for all PolarH10 Mermaid diagrams.

The repo keeps the README-side Mermaid footprint compact and pushes the larger,
more operational diagrams into `docs/diagrams/`. That keeps GitHub-native
rendering readable while still giving the Pages site a deeper diagram set.

## Quick start

```powershell
# From repo root
npm install            # first time only
npm run diagram:dev    # renders SVGs + opens live viewer
```

Or open `viewer.html` directly — it will live-render `.mmd` sources via the
Mermaid JS CDN even without pre-rendered SVGs.

## Diagram categories

| Category      | Diagrams                                   |
| ------------- | ------------------------------------------ |
| Architecture  | repo-structure · code-architecture · data-flow |
| Runtime       | session-lifecycle |

## Adding a new diagram

1. Create `docs/diagrams/your-diagram.mmd`
2. Add an entry to `docs/diagrams/manifest.json`
3. Run `npm run diagram:render:all` to generate the SVG
4. Run `npm run diagram:sync:readme` if the diagram is one of the README blocks
5. The viewer picks it up automatically from the manifest

## Diagram style guardrails

- Keep the README diagram compact and structural; move dense operational views into `docs/diagrams/`.
- Prefer flowcharts for repo layout and runtime handoff diagrams.
- Keep labels short enough for GitHub-native Mermaid rendering.
- Use the shared Polaroid-inspired theme in `mermaid.config.json` instead of per-diagram visual drift.

## Files

| File                  | Purpose                                      |
| --------------------- | -------------------------------------------- |
| `manifest.json`       | Registry of all diagrams — drives the viewer |
| `mermaid.config.json` | Shared Mermaid render config                 |
| `viewer.html`         | Manifest-driven viewer with pan/zoom + fullscreen |
| `*.mmd`               | Mermaid diagram sources                      |
| `*.svg`               | Pre-rendered SVGs (generated, do not edit)   |
