---
sidebar_position: 6
title: CLI reference
description: "The prototest CLI: the four verbs, their arguments, the environment targets and the exit codes."
---

# CLI reference

`prototest` reads ProtoTest evidence from a terminal: one run summary, a static page over a folder of runs, a pull request verdict over two reports, and the feedback digest. It needs no agent and no browser. The [feedback action](../continuous-integration/index.md#the-feedback-action) installs it and calls the same commands in CI, so a local run and a CI step read the same archive the same way.

## Install

```bash
dotnet tool install --global ProtoTest.Cli
```

The tool command is `prototest`. The package targets .NET 8; on a machine with only a newer runtime, set `DOTNET_ROLL_FORWARD=LatestMajor` so the tool starts. The action sets that for you.

## The verbs

Usage:

```text
usage: prototest summary <file.prototrace>
       prototest index <folder>
       prototest feedback <file.prototrace> [--digest <path>]
       prototest verify <baseline-report.json> <current-report.json>
```

An unknown verb, or the wrong arguments, prints that usage to stderr and exits `1`.

### summary

Reads one trace and prints the deterministic diagnosis as text: the run id, the outcome counts, every test that did not fully succeed with its error, source location and failing operation, and the run gates. It is the document `get_diagnosis` returns as JSON. [Diagnosis](./diagnosis.md) explains the fields and the rules.

```bash
prototest summary TestResults/ProtoTest/run.prototrace
```

Exit `0` when the summary printed. Exit `1` when the file is missing or cannot be read, with the reason on stderr.

### index

Discovers the `.prototrace` archives under the folder, newest first, and writes the evidence into a folder you can share:

- `index.html` in the folder, listing each run's outcome counts, the tests that did not pass, links to the trace and its digest, and every archive that could not be read with the reason.
- A `.digest.json` file beside each archive, the diagnosis JSON the page links to.

Discovery looks at the folder's `TestResults/` first and then walks the tree; [Setup](./setup.md#where-it-reads) has the details. The command prints the count and the path of the page it wrote, and names any archive it had to skip.

Exit `0` when the page is written. Exit `1` when the folder is missing, holds no readable archive, or the page cannot be written.

### verify

Compares two JSON reports from a `ProtoTest.Reporting` sink:

```bash
prototest verify baseline.json TestResults/ProtoTest/report.json
```

The baseline is the report from the default branch and the current report is the run under review. Each failing finding prints one `::error` workflow command, which a GitHub runner turns into an annotation, and the verdict lists the findings and the coverage deltas. The default severities make the verb a pull request gate: `regressed`, `stale-spec` and `gate-failed` fail, and `added-uncovered` warns. [Verification](./verification.md) explains each class and the specification identity.

Exit `0` when no finding is a fail and `1` when one is. Exit `1` also when a report file is missing or cannot be read. The verb takes no specification candidates in 1.1; a candidate comparison is a library call ([Verification](./verification.md#the-specification-identity)).

### feedback

Reads one run's digest and posts it:

```bash
prototest feedback TestResults/ProtoTest/run.prototrace --digest digest.json
```

- The annotations go to stdout as `::error` workflow commands, one per failing test and per failed run gate.
- The pull request comment posts the digest and the trace link when the GitHub targets are complete.
- The webhook posts the digest JSON to the configured address.
- `--digest` writes the digest JSON to the path.

Every channel reports its outcome on stderr: `posted`, `skipped` with the reason, or `failed`. A channel with no target, or nothing to post, skips. Only a channel that reached its target and failed makes the verb exit `1`. With no target configured the verb is safe to run locally.

## Environment targets

`feedback` reads its channel targets from the environment. The names are the GitHub Actions convention, so CI needs no extra inputs.

| Variable | Channel | Meaning |
| --- | --- | --- |
| `GITHUB_TOKEN` | comment | the token that posts the comment; the workflow needs `issues: write` |
| `GITHUB_REPOSITORY` | comment | the repository as `owner/name` |
| `GITHUB_EVENT_PATH` | comment | the event payload file; the pull request number is read from it |
| `GITHUB_API_URL` | comment | the GitHub REST base URL; defaults to `https://api.github.com` |
| `PROTOTEST_FEEDBACK_TRACE_URL` | comment | the artifact URL the comment links to |
| `PROTOTEST_FEEDBACK_WEBHOOK_URL` | webhook | the address the digest JSON is posted to |
| `PROTOTEST_FEEDBACK_WEBHOOK_SECRET` | webhook | the shared-secret header value; no header is sent without it |
| `PROTOTEST_FEEDBACK_WEBHOOK_SECRET_HEADER` | webhook | the shared-secret header name; defaults to `X-ProtoTest-Secret` |

The [feedback action](../continuous-integration/index.md#the-feedback-action) maps its inputs to these names, so a local command and the action take the same path. [Loop](./loop.md) shows the workflow and what the reviewer sees.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | the verb did its job |
| `1` | the input was missing or unreadable; `index` found no readable archive or could not write the page; the verdict has a fail finding; or a feedback channel that reached its target failed |

## Limits

- The verbs read files. The only writes are the `index` page, the digests beside the traces and the `--digest` file.
- No network call happens unless a target is configured. A missing target is a named skip, never a failure.
- The verbs take no other arguments, and there is no verb that reruns a suite, writes a trace or changes an archive.
- The digest is built from the written archive after the run, so it reflects what the run recorded ([Loop](./loop.md#limits)).
- The four verbs are the whole `prototest` surface in 1.1.

Run `prototest summary` over the newest archive, or `prototest index` over the results folder, and the same evidence your agent reads is on your terminal.
