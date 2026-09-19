# ProtoTrace Viewer

Static Vue 3 viewer for `.prototrace` archives. Trace files are read entirely in browser memory and are never uploaded. The UI combines ProtoTest's blueprint visual language with the readable light and dark surfaces of the HTML report.

## Design

The viewer uses the ProtoTest design system from the [Complete Design Handoff v6](../assets/internal/prototest-complete-design-handoff-v6.html):

- **Colors**: Navy/Blue brand palette with cyan active connections, green verified state
- **Typography**: Manrope for body, Space Grotesk for headings, JetBrains Mono for code/trace
- **Light/Dark**: Respects system preference, with full dark mode support
- **Grid**: Subtle blueprint grid background pattern

Tests are grouped by fixture/file with compact method names. Per-test tabs separate the overview, lifecycle, complete trace, network traffic, assertions, observations, artifacts, resources (what the test owned and released) and diagnostics (failures, findings and gate verdicts). The run itself has a section too: the infrastructure it owned, and how its gates judged it. Parallel tests receive separate run-timeline lanes, while the inspector provides a navigable ancestor/descendant tree. Categories are derived from trace namespaces, so integrations can add new kinds without requiring a viewer release. JSON diagnostics and shape comparisons render as expandable structured data.

The empty state can open the bundled `public/demos/prototest-demo.prototrace`. It is a real, complete parallel run of the unified SaaS demo: successful scenarios, partial diagnostic scenarios, one intentional shape-mismatch failure, and the run's own gate verdict. The number of scenarios grows with the demo, so the regeneration script checks a floor and the single intentional failure rather than exact totals. It uses the same archive reader as a user-selected trace.

Regenerate it with `./eng/update-viewer-demo.ps1`. Set `ProtoTest:Database=postgres` (or `ProtoTest__Database=postgres`) to add a database container to the run's activity (that needs a container runtime).

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
