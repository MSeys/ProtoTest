# ProtoTest.Cli

The `prototest` .NET tool.

```
dotnet tool install --global ProtoTest.Cli
prototest trace summary TestResults/Shop.prototrace
```

`trace summary` prints the failure digest — the run, the outcome counts, and for every test that did not
fully succeed its error, source location and failing operation — so a trace is useful from a CI log or an
agent without opening the viewer.

## Limits

- One command today: `trace summary <file>`. The trace reader underneath (`ProtoTest.Traces`) is the
  foundation for richer commands and the MCP server.
