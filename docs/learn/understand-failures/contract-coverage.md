---
id: contract-coverage
title: What did my suite never check?
sidebar_label: Contract coverage
sidebar_position: 6
description: "Read what a run checked about your API's contract, endpoints, statuses and fields, next to what it only saw."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# What did my suite never check?

<Lesson
  track="Understand failures"
  step="Lesson 6 of 8"
  minutes={8}
  outcomes={[
    'Tell contract coverage from code coverage',
    'Read a coverage row and a traffic row in the run report',
    'Say which assertion claims a response field',
  ]}
  needs={[
    <>The earlier lessons, <Link to="/learn/understand-failures/run-gates">run gates</Link> for the gate row</>,
    'Nothing installed. The archive and its report are on this site.',
  ]}
/>

## The problem

An endpoint can be called by a hundred setup helpers and have its response asserted by none of them. The status comes back, the fields go unread, and a renamed field breaks a client you never tested.

Code coverage cannot see this. It counts lines that ran, not whether a check read what they returned. Contract coverage counts the endpoints, statuses and fields your assertions actually matched.

## Do it

### 1. Open the report from a real run

Download [l4-coverage.prototrace](pathname:///lessons/l4-coverage.prototrace). The file is a zip archive. Open the report inside it at `resources/run/JsonReportSink/run-artifact-1/report.json`. A local run writes the same data to `TestResults/Northstar.ProtoTest/report.json`.

The journey writes a project over REST and reads it back over GraphQL. Look for these rows:

| Row | Reading |
| --- | --- |
| `coverage` REST `POST /api/v1/projects`, covered | the endpoint and the status the test asserted |
| `traffic` REST `POST /api/v1/projects · 201`, with `$.id`, `$.name`, `$.slug`, `$.status`, `$.environmentCount`, `$.createdAtUtc` | the fields the response carried and no assertion mentioned |
| `gate` `no error findings`, passed | the run gate from the last lesson |

The summary reads `CoverageTotal: 1`, `Covered: 1`, `Uncovered: 0`. It counts the write endpoint only. The six fields are not part of it.

### 2. Read the two rows in the JSON

```json
{ "TargetName": "Northstar:Northstar", "Category": "REST",
  "Identifier": "POST /api/v1/projects", "Kind": "coverage",
  "Status": "Success", "IsCovered": true }
{ "TargetName": "Northstar:Northstar", "Category": "REST traffic",
  "Identifier": "POST /api/v1/projects · 201", "Kind": "traffic",
  "Status": "Neutral",
  "Message": "Fields that arrived in a response but no shape assertion mentioned.",
  "Children": ["$.id", "$.name", "$.slug", "$.status", "$.environmentCount", "$.createdAtUtc"] }
```

The first row is the covered claim. The second is the gap.

### 3. Find which assertion claims a field

- `Should.HaveHttpStatus(...)` covers the endpoint and the status.
- `Should.MatchShape(...)` covers every property path it matched, such as `name` and `status`.
- `JsonValue.Any()` mentions a whole value but not the fields inside it, so those fields stay in the traffic section.

Compare the traffic row with the shape assertions in your own journeys. Every field it lists is a field no assertion named.

## What happened

Two collectors produced those rows. The composition registers both on the REST client:

<AnnotatedCode
  filename="Setup.cs"
  code={`app.AddRest(rest => rest
        .CaptureAttachments()
        .AddClient(NorthstarTargets.Api)
        .AddCollector<RestCoverageCollector>()
        .AddCollector<RestTrafficCoverageCollector>())`}
  callouts={[
    {line: 2, title: 'Keep the evidence', note: 'Request and response artifacts land in the trace, which is what the fields are read from.'},
    {line: 4, title: 'Count what was called', note: 'RestCoverageCollector reports every REST endpoint the suite called, with its hit count.'},
    {line: 5, title: 'List what was only seen', note: 'RestTrafficCoverageCollector reports the response fields no shape assertion mentioned, in their own section. It never counts them as covered.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. Collectors gather across the run; the sinks registered below them write the report.</>}
/>

A covered row says what the suite checked. A traffic row says what it only saw. A field no assertion names can break silently, and the traffic section shows that gap without pretending it is covered.

## Check yourself

<Checkpoint
  question="The REST write asserts only the status, and the report lists six response fields as unasserted. The GraphQL read of the same project asserts its name and status. Why are the REST fields still unasserted?"
  verify={<>Open the report in <a href="pathname:///lessons/l4-coverage.prototrace">l4-coverage.prototrace</a> and find the REST traffic row.</>}>

Coverage is kept per target and per protocol. A GraphQL shape assertion claims GraphQL fields. It says nothing about a REST response.

The REST write response was only judged on its status, so every field it carried is listed in the traffic section as observed and unasserted. Adding one `Should.MatchShape` to the write moves the fields it names into the covered row.

</Checkpoint>

## Remember

- Code coverage says lines ran. Contract coverage says what the suite checked about the API.
- A status check covers the endpoint and the status. A shape assertion claims a field.
- The traffic section lists what arrived in a response and no assertion mentioned.

## Go deeper

- [Coverage and observations](/docs/observability/coverage): collectors for other contracts, such as OpenAPI documents and GraphQL schemas, and how to write your own.
