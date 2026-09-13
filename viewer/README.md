# ProtoTrace Viewer

Static Vue 3 viewer for `.prototrace` archives. Trace files are read entirely in browser memory and are never uploaded. The UI combines ProtoTest's blueprint visual language with the readable light and dark surfaces of the HTML report.

Tests are grouped by fixture/file with compact method names. Per-test tabs separate the overview, lifecycle, complete trace, network traffic, assertions, observations, artifacts, and errors. Parallel tests receive separate run-timeline lanes, while the inspector provides a navigable ancestor/descendant tree. Categories are derived from trace namespaces, so integrations can add new kinds without requiring a viewer release. JSON diagnostics and shape comparisons render as expandable structured data.

The empty state can open the bundled `public/demos/prototest-demo.prototrace`. It is a real, complete parallel run of the unified SaaS demo: fifteen successful scenarios, two partial diagnostic scenarios, and one intentional shape-mismatch failure. It uses the same archive reader as a user-selected trace.

The Cloudflare Pages security headers allow `connect-src 'self'` solely so the viewer can fetch that same-origin static demo archive. User-selected traces are still read directly from browser memory and never uploaded.

Regenerate it from the repository root with `./eng/update-viewer-demo.ps1`.

```bash
npm install
npm run dev
```

Create the production output with:

```bash
npm run build
```

Deploy the generated `dist` directory to Cloudflare Pages. No Functions, storage, or server runtime is required.
