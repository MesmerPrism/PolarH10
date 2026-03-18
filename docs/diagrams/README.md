# Diagram Viewer

Interactive browser-based viewer for all PolarH10 Mermaid diagrams.

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

## Adding a new diagram

1. Create `docs/diagrams/your-diagram.mmd`
2. Add an entry to `docs/diagrams/manifest.json`
3. Run `npm run diagram:render:all` to generate the SVG
4. The viewer picks it up automatically from the manifest

## Files

| File                  | Purpose                                      |
| --------------------- | -------------------------------------------- |
| `manifest.json`       | Registry of all diagrams — drives the viewer |
| `mermaid.config.json` | Shared Mermaid render config                 |
| `viewer.html`         | Manifest-driven fullscreen viewer            |
| `*.mmd`               | Mermaid diagram sources                      |
| `*.svg`               | Pre-rendered SVGs (generated, do not edit)   |
