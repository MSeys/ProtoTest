# ProtoTest.Mcp.DemoEndpoint

A demo-only MCP endpoint. It serves the same read-only tools as the local `prototest-mcp` server (`list_runs`, `get_failure`, `get_diagnosis`, `get_coverage`, `get_suite_map`) over the Streamable HTTP transport, against the one bundled `viewer/public/demos/prototest-demo.prototrace` archive.

```bash
dotnet run --project samples/ProtoTest.Mcp.DemoEndpoint
```

The MCP endpoint is then at `http://127.0.0.1:5199/`. It serves at the root path `/` (the `MapMcp` default); a POST to `/mcp` returns 404.

## Limits

- **No public host.** The endpoint runs locally for demos; the stdio server stays the product surface.
- **Read-only over one fixed trace.** There is no filesystem input, no accounts, no uploads and no retention (the transport runs in stateless mode).
- **Rate-limited** per caller (60 requests per minute, fixed window) and bound to loopback.

Hosting this beyond a demo (an address, a proxy, retention and abuse limits) is a deployment decision, not a package.
