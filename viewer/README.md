# ProtoTrace Viewer

A static app that opens `.prototrace` files created by ProtoTest. It shows the test lifecycle, operations, checks, observations, resources, attachments and gate results in one place.

**[Open the interactive demo](https://trace.prototest.dev/?demo=1)**

## Open a trace

Drop a `.prototrace` file on [trace.prototest.dev](https://trace.prototest.dev). Nothing is uploaded. The file opens in browser memory.

Treat a trace as test output. It can contain application data, so check what was captured before sharing it.

## Read a run

The run leads one test list, with shared search and outcome filters, and splits into views: Overview (Needs attention, with the diagnosis rules `prototest summary` uses, beside what the run could see), Timeline (tests and untraced gaps on the run clock), Operations (the run's own work and tracked items), Details (the run id and every `environment.*` value) and Files.

Each test has four views:

- **Steps** opens on the test body. Setup and teardown are summary rows that expand along a failure. Gaps get their own rows.
- **Timeline** places every operation on the test clock. Zoom to a phase or search.
- **State** puts lifelines and changes on that clock. Selecting a change opens its operation and highlights what it touched; selecting an item highlights its operations in Timeline.
- **Evidence** orders observations, files, findings and moments by time, with the operation that recorded each.

The **Framework** switch beside the view tabs shows, dims or hides framework operations in Steps and Timeline, and the machinery a test ran on in State, and remembers the choice.

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

Supported spreadsheet artifacts can also be previewed without leaving the viewer. `npm run scale:trace` builds a 1,000+ test trace into the gitignored `.perf` directory, and `npm run scale:measure` times the built viewer against it in Edge. Numbers and method are under [Performance at 1,000+ tests](#performance-at-1000-tests).

## Performance at 1,000+ tests

The viewer was measured on a generated 1,200-test trace derived from the committed demo trace.
`viewer/scripts/generate-scale-trace.mjs` clones each demo test until the requested count, rewriting ids,
names and timestamps, and writes small placeholder payloads for the artifacts. Every test keeps the demo's
real operations, checks, observations, sections, events and state, so the viewer exercises its real paths.
The generated file is 8.6 MB zipped (57.8 MB of spans JSON, 35.6 MB of state JSON). It is a heavier suite
per test than the trace-size table on the [benchmarks page](https://prototest.dev/docs/project/benchmarks#how-big-a-trace-gets), which counts one operation per test.

Method: a production build (`npm run build`), Microsoft Edge 154.0.4258.37 headless at 1440x900, no CPU or
network throttling, on the AMD Ryzen 7 9800X3D machine the benchmarks page uses. Each number is the median of five cold runs: a fresh
page, the trace loaded through the viewer's own file input, then one interaction. The harness is
`viewer/scripts/measure-viewer.mjs`. It serves the built `dist` over loopback, so no dev server is involved.
Before and after the pass:

| Step | Before | After |
| --- | --- | --- |
| Open the trace and render the run list | 1,356 ms | 1,316 ms |
| Filter to "Needs attention" | 77 ms | 53 ms |
| Clear the filter (1,200 rows return) | 182 ms | 162 ms |
| Type "GraphQL" in the run search | 365 ms | 231 ms |
| Open a test with 169 operations | 167 ms | 151 ms |
| Return to the run list | 650 ms | 566 ms |

Read plainly: a cold open of a suite this size takes about **1.3 seconds**, and the remaining run list
steps land between **50 ms and 570 ms**. The test views stay fast at this size: Timeline, State and the
inspector all answer in 33 to 50 ms for a test with 169 operations.

What the pass changed, all inside the existing views:

- Each test computes its display name, title, group and search text once and caches it, instead of
  re-deriving them for every row on every render.
- The run view is kept alive when a test opens, so returning moves the cached DOM instead of rebuilding
  a thousand rows.
- Each run and rail row contains its own layout and paint (`contain: layout paint`), so one row's change
  never re-lays-out the list.

What remains, with the measurement that shows it:

- The cold open is dominated by reading and parsing the two JSON documents and the first full layout
  (five long tasks, the longest about 550 ms). Removing that needs a streaming reader.
- The run list and the outcome strip render one element per test, so returning to the run and changing
  the filter move about a thousand DOM rows. Removing that needs a windowed list.
- `content-visibility: auto` on the rows was measured and rejected. It skipped off-screen layout on the
  first render but made typed filtering about twice as slow, because every row added or removed during
  filtering pays its bookkeeping.


The viewer numbers re-run from the `viewer` directory:

```
npm run scale:trace
npm run build
npm run scale:measure -- --trace .perf/scale-1200.prototrace --tests 1200 --runs 5
```

The generator prints the trace's size. The harness prints every run and its median, and `--profile` also
writes CPU profiles and renderer time (script, layout, style) per measured step. Neither script is part of
the build or CI. The harness needs an installed Chromium-family browser (Edge by default) and
`puppeteer-core`, which is a viewer dev dependency.

## Learn more

- [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- [Repository contribution guide](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md)
- [Issues](https://github.com/MSeys/ProtoTest/issues)
