# ProtoTrace Viewer

A static application for opening `.prototrace` files created by ProtoTest.

**[Open the interactive demo](https://trace.prototest.dev/?demo=1)**

## What is it for?

A failed assertion is not always enough to explain what happened. The viewer shows the test lifecycle, operations, checks, observations, resources, attachments, coverage and gate results in one place.

Supported spreadsheet artifacts can also be previewed without leaving the viewer.

## Does it upload the trace?

No. The file is opened in browser memory.

It may still contain application data. Treat it as a test artifact and check what was captured before sharing it.

## Run locally

```bash
npm install
npm test
npm run dev
```

Create the production output with `npm run build`. The generated `dist` directory is static and does not require a server runtime.

## Learn more

- [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- [Repository contribution guide](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md)
- [Issues](https://github.com/MSeys/ProtoTest/issues)
