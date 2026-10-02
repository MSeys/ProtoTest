# ProtoTest.Cli

> Preview: the surface can change before 1.2.

The `prototest` .NET tool.

```bash
dotnet tool install --global ProtoTest.Cli
prototest summary TestResults/Shop.prototrace
prototest index TestResults
prototest feedback TestResults/Shop.prototrace --digest digest.json
prototest verify baseline.json current.json
prototest compare main.prototrace TestResults/Shop.prototrace
prototest prove before.prototrace after.prototrace --test "orders are listed"
prototest review TestResults/Shop.prototrace
```

- `summary` prints the failure digest: the run, the outcome counts, and for every test that did
  not fully succeed its error, source location, selected failure and the cause the diagnosis found, so
  a trace is useful from a CI log or an agent without opening the viewer.
- `index` writes a static `index.html` over a folder's runs: each run's id, start and completion
  time, outcome counts, the tests that did not pass, and links to its `.prototrace` and its
  `<trace>.digest.json` digest. An archive the reader cannot open is listed on the page with the
  reason. The page and the digests are plain files beside the traces, so a folder of evidence can be
  shared without a server.
- `feedback` builds the same digest and posts it through the `ProtoTest.Feedback` channels. The
  annotations go to stdout; the per-channel outcomes go to stderr. `--digest <path>` writes the digest
  JSON as well; `--baseline <trace>` adds which tests the run broke or fixed against that trace.
- `verify` compares two JSON reports a `ProtoTest.Reporting` sink wrote, as files or embedded in two
  traces, and returns the
  `ProtoTest.Verification` verdict: exit 0 when nothing fails, exit 1 when a finding is `fail`, with
  one `::error` annotation per failing finding on stdout.
- `compare` compares two traces test by test: which tests broke, were fixed or still fail, and for
  each the first operation where the two runs part. Exit 1 when a test broke.
- `prove` proves a fix: the claimed tests failed in the baseline and succeed in every current run,
  nothing broke, and the embedded reports verify. Exit 1 with the reasons when it is not proven.
- `review` says what each test proves: a body with no check, a call no check looked at, or an
  untraced gap, each with the next step. It advises and exits 0.

The feedback targets follow the GitHub Actions environment: `GITHUB_TOKEN`, `GITHUB_REPOSITORY`,
`GITHUB_EVENT_PATH` (the pull request number) and `GITHUB_API_URL` for the comment;
`PROTOTEST_FEEDBACK_WEBHOOK_URL`, `PROTOTEST_FEEDBACK_WEBHOOK_SECRET` and
`PROTOTEST_FEEDBACK_WEBHOOK_SECRET_HEADER` for the webhook; `PROTOTEST_FEEDBACK_TRACE_URL` is the
artifact link the comment carries. A missing target skips its channel with the reason.

## Limits

- Seven commands: `summary <file>`, `index <folder>`, `feedback <file> [--digest <path>] [--baseline <trace>]`,
  `verify <baseline> <current>`, `compare <baseline.prototrace> <current.prototrace>`,
  `prove <baseline.prototrace> <current.prototrace>... [--test <name>]...` and
  `review <file.prototrace> [--test <name>]...`. The models underneath (`ProtoTest.Diagnosis`,
  `ProtoTest.Traces`, `ProtoTest.Feedback`, `ProtoTest.Verification`) are the same ones the MCP tools
  use, so the surfaces cannot drift apart.
- `index` writes only: `index.html` in the named folder and one digest beside each run's archive. It
  never renames, moves or deletes an archive, and it carries no JavaScript.
- `verify` reads reports, as files or embedded in traces; a run embeds one with a `ProtoTest.Reporting` sink. Spec
  candidate files are a library capability today, not a verb option.
- The tool targets net8.0; a newer runtime runs it with `DOTNET_ROLL_FORWARD=LatestMajor`.
