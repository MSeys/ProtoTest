# ProtoTest.Feedback

> Preview: the surface can change before 1.2.

Posts a failing run's digest where a pull request reads it: a comment with the trace link, check
annotations and a webhook for a machine consumer. The digest is the `ProtoTest.Diagnosis` document,
so the comment, the CLI and the MCP tools cannot tell different stories.

```bash
dotnet add package ProtoTest.Feedback
```

```csharp
var digest = ProtoFeedback.ReadDigest("TestResults/Shop.prototrace");
var report = await ProtoFeedback.PostAsync(
    digest,
    new ProtoFeedbackTarget
    {
        Token = token,
        Repository = "owner/repo",
        PullRequestNumber = 7,
        TraceLink = "https://github.com/owner/repo/actions/runs/1/artifacts/2"
    },
    httpClient,
    Console.Out);

foreach (var channel in report.Channels)
{
    Console.WriteLine($"{channel.Channel}: {channel.Status} {channel.Reason}");
}
```

The channels are pure functions of the digest and report one outcome each:

| Channel | Posts | Skips when |
| --- | --- | --- |
| `github-annotations` | one `::error` per failing test, with its source location, and one per failed run gate | the run has no failures |
| `github-pr-comment` | the Markdown digest with the trace link, to `POST /repos/{repo}/issues/{number}/comments` | no token, no repository, no pull request number, or a run with no failures |
| `webhook` | the digest JSON to the configured address, with an optional shared-secret header | no webhook URL |

`prototest feedback <file.prototrace>` runs the same post, reading the targets from the environment;
see `ProtoTest.Cli`.

## Limits

- Post-run by design: the digest is read from the written `.prototrace` archive. A run-time sink
  cannot be the mechanism, because the archive is written after sinks and a failed assertion is not a
  report item.
- One digest: `ProtoDiagnosis.Read` builds it from the archive and its embedded report. The channels
  render it and never recompute coverage or the failure selection.
- Nothing leaves the machine by default: no target means no post, and the annotations are written to
  the writer the caller gives. A refused target fails its channel, named, instead of being swallowed.
- The comment posts only when the digest carries a failure or a failed gate; a green run's status
  check is the report. The webhook posts every digest, because a machine consumer decides.
- The shared-secret header carries the secret value; it is not an HMAC signature.
- A skipped test is not annotated: it did not fail.

## Learn more

- [`prototest summary` and the trace format](https://prototest.dev/docs/observability/prototrace)
