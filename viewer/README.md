# ProtoTrace Viewer

A static application for opening `.prototrace` files created by ProtoTest.

**[Open the interactive demo](https://trace.prototest.dev/?demo=1)**

## What is it for?

A failed assertion is not always enough to explain what happened. The viewer shows the test lifecycle, operations, checks, observations, resources, attachments and gate results in one place. Coverage reports travel in the archive as the JSON and HTML report files, listed with the run's other attachments.

Supported spreadsheet artifacts can also be previewed without leaving the viewer.

## Does it upload the trace?

No. The file is opened in browser memory.

It may still contain application data. Treat it as a test artifact and check what was captured before sharing it.

## Bundled demos

The start screen lists the bundled demos with their test counts, read from the traces themselves.
This includes a genuine OpenCSMS product run alongside the Northstar samples. To add one, drop the
`.prototrace` file in `public/demos/` and add one entry to the registry in `src/demos.ts` with the
key, the file name, the label, and a one-line description. The entry opens under `?demo=<key>`.

To refresh a product demo, run its suite, replace the file in `public/demos/`, and keep the entry.
Do not bundle a trace that misrepresents the product: a synthetic benchmark trace is not a product
demo.

## Sharing a trace

Two query parameters make a trace shareable:

- `?demo=<key>` opens a bundled demo (`?demo=1` is the full demo). Anyone with the viewer link sees
  the same trace.
- `?trace=<absolute-url>` fetches a trace hosted anywhere that allows cross-origin reads, for example
  a `raw.githubusercontent.com` URL of a committed trace. The host must send CORS headers; when the
  fetch fails the viewer says so, and a downloaded copy still opens by dropping it in.

The header's Copy link button copies the demo or trace URL behind the open trace, keeping the
reader's test and selection. A trace opened from a local file has no URL to share, so the button
stays hidden there.

## Run locally

```bash
npm ci
npm test
npm run dev
```

Create the production output with `npm run build`. The generated `dist` directory is static and does not require a server runtime.

## Measure it at scale

`npm run scale:trace` builds a 1,000+ test trace from the bundled demo trace into the gitignored `.perf`
directory, and `npm run scale:measure` times the built viewer against it in Edge. The measured numbers and
the method live on the [benchmarks page](https://prototest.dev/docs/project/benchmarks#the-viewer-at-1000-tests).

## Learn more

- [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- [Repository contribution guide](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md)
- [Issues](https://github.com/MSeys/ProtoTest/issues)
