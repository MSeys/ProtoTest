# ProtoTrace Viewer

A static app that opens `.prototrace` files created by ProtoTest. It shows the test lifecycle, operations, checks, observations, resources, attachments and gate results in one place.

**[Open the interactive demo](https://trace.prototest.dev/?demo=1)**

## Open a trace

Drop a `.prototrace` file on [trace.prototest.dev](https://trace.prototest.dev). Nothing is uploaded. The file opens in browser memory.

Treat a trace as test output. It can contain application data, so check what was captured before sharing it.

## Read a run

The run leads one test list, with shared search and outcome filters. Needs attention uses the same diagnosis rules as `prototest summary`. The run timeline marks untraced gaps; run operations and tracked items open in the inspector. Run details lists the run id and every `environment.*` value.

Each test has four views:

- **Steps** opens on the test body. Setup and teardown are summary rows that expand along a failure. Gaps get their own rows.
- **Timeline** places every operation on the test clock. Zoom to a phase, search, or dim or hide framework operations.
- **State** puts lifelines and changes on that clock. Selecting a change opens its operation and highlights what it touched; selecting an item highlights its operations in Timeline.
- **Evidence** orders observations, files, findings and moments by time, with the operation that recorded each.

The inspector shows request and response, comparisons, state changes, metadata and moment sections. Its section index jumps to each part. Binary bodies recorded as text are marked instead of displayed as broken glyphs.

Test routes are `#/test/<id>/steps|timeline|state|evidence`. Old `story`, `spans` and `files` links remain aliases. A run selection uses `#/?span=<id>` or `#/?kind=<kind>&item=<id>`.

Untraced time is derived by the viewer, not recorded on the wire. A gap inside a phase is shown from 15% of that phase, at least 20 ms, and always from 250 ms. It means no operation was recorded, not that nothing happened.

## Share a trace

Two query parameters make a trace shareable:

- `?demo=<key>` opens a bundled demo (`?demo=1` is the full demo). Anyone with the link sees the same trace.
- `?trace=<absolute-url>` fetches a trace hosted anywhere that allows cross-origin reads, for example a `raw.githubusercontent.com` URL. When the fetch fails the viewer says so, and a downloaded copy still opens by dropping it in.

The header's Copy link button copies the URL behind the open trace, keeping the reader's test and selection. A trace opened from a local file has no URL to share, so the button stays hidden there.

## Run locally

```bash
npm ci
npm test
npm run dev
```

Create the production output with `npm run build`. The generated `dist` directory is static and needs no server runtime.

## Add a demo

The start screen lists the bundled demos with their test counts, read from the traces themselves. To add one, drop the `.prototrace` file in `public/demos/` and add one entry to the registry in `src/demos.ts` with the key, the file name, the label and a one-line description. The entry opens under `?demo=<key>`.

To refresh a product demo, run its suite and replace the file in `public/demos/`. Do not bundle a trace that misrepresents the product: a synthetic benchmark trace is not a product demo.

Supported spreadsheet artifacts can also be previewed without leaving the viewer. `npm run scale:trace` builds a 1,000+ test trace into the gitignored `.perf` directory, and `npm run scale:measure` times the built viewer against it in Edge. Numbers and method live on the [benchmarks page](https://prototest.dev/docs/project/benchmarks#the-viewer-at-1000-tests).

## Learn more

- [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- [Repository contribution guide](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md)
- [Issues](https://github.com/MSeys/ProtoTest/issues)
