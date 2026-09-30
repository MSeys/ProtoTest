---
id: contract-coverage
title: Contract coverage, not code coverage
sidebar_label: Contract coverage, not code coverage
sidebar_position: 2
description: "Read what a run checked about your API's contract: endpoints, statuses and fields, next to what it merely saw."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Contract coverage, not code coverage

Code coverage counts the lines that ran. It does not say whether a check read what they returned. The report this lesson reads answers the other question, and the gap it shows is the interesting part.

<LearnShell
  level="Level 4, lesson 2"
  minutes="About 8 minutes"
  outcome={[
    'Tell contract coverage apart from code coverage.',
    'Read a coverage row and a traffic row in the run report.',
    'Say which assertion claims a response field.',
  ]}
  before={[
    <>Read a failing trace (<Link to="/learn/evidence/read-a-failing-trace">lesson 1</Link>).</>,
    'Nothing installed. The archive and its report are on this site.',
  ]}
  situation={
    <>
      <p>An endpoint can be called by a hundred setup helpers and have its response asserted by none of them. The status comes back, the fields go unread, and a rename in the response body breaks a client you never tested.</p>
      <p>ProtoTest measures coverage against the contract instead: the endpoints, the statuses and the fields your assertions actually matched. The sample records one REST write and one GraphQL read, and the report it wrote shows both what was covered and what was only seen.</p>
    </>
  }
  checkpoint={{
    question:
      'The write asserts only the status, and the report lists six response fields as unasserted. The GraphQL read of the same project asserts its name and status. Why are the REST fields still unasserted?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l4-coverage.prototrace">l4-coverage.prototrace</a>. The file is a zip archive; open the report it carries at{' '}
        <code>resources/run/JsonReportSink/run-artifact-1/report.json</code>, or read the same data in the HTML report beside it.
      </>
    ),
    reveal: (
      <>
        Coverage is kept per target and per protocol. A GraphQL shape assertion claims GraphQL fields; it says nothing about a REST response. The REST write's response was only ever judged on its status, so every field it carried is listed in the traffic section as observed and unasserted. Adding one <code>Should.MatchShape</code> to the write moves the fields it names into the covered row.
      </>
    ),
  }}
  learned={[
    'Code coverage says lines ran; contract coverage says what the suite checked about the API.',
    'A status check covers the endpoint and the status; a shape assertion is what claims a field.',
    'The traffic section lists what arrived in a response and no assertion mentioned.',
  ]}
  next={[
    {
      label: 'The archive and the reports',
      to: '/learn/evidence/artifacts-and-reports',
      note: 'One file from CI holds the trace, the reports and the attachments. This lesson opens it.',
    },
    {
      label: 'Coverage and observations',
      to: '/docs/observability/coverage',
      note: 'Collectors, traffic coverage, and how to write your own.',
    },
  ]}>

## Where the coverage comes from

The composition registers two collectors on the REST client:

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

## The report this run wrote

The journey writes a project over REST and reads it back over GraphQL. The report inside its archive holds:

| Row | Reading |
| --- | --- |
| `coverage` REST `POST /api/v1/projects`, covered | the endpoint and the status the test asserted |
| `traffic` REST `POST /api/v1/projects · 201`, with `$.id`, `$.name`, `$.slug`, `$.status`, `$.environmentCount`, `$.createdAtUtc` | the fields the response carried and no assertion mentioned |
| `gate` `no error findings`, passed | the run gate the sample registers; the gate row is explained in [lesson 4](/learn/evidence/read-the-findings-and-the-run-gate) |
| `resource` rows for the run pieces | what the run owned, still registered when the report was written |

The summary reads `CoverageTotal: 1`, `Covered: 1`, `Uncovered: 0`. That number is narrow: one unit of the contract was checked, the write's endpoint. The six fields are not part of it, and the traffic section says so.

The two rows read like this in `report.json` (from <a href="pathname:///lessons/l4-coverage.prototrace">l4-coverage.prototrace</a>, `resources/run/JsonReportSink/run-artifact-1/report.json`):

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

The first row is the covered claim: the endpoint and the status the write asserted. The second row is the gap: six fields the response carried that no shape assertion mentioned. The children are shown by their identifiers; each child in the file is a full traffic row with its own status. A covered row says what the suite checked; a traffic row says what it only saw.

## What claims a field

The rule is short:

- `Should.HaveHttpStatus(...)` covers the endpoint and the status.
- `Should.MatchShape(...)` covers every property path it matched, such as `name` and `status`.
- `JsonValue.Any()` mentions a whole value but not the fields inside it, so those fields stay in the traffic section.

A field that no assertion names is a field that can break silently. The traffic section is where the report shows that gap without pretending it is covered.

## Reading it in your own run

The sample writes its report to `TestResults/Northstar.ProtoTest/report.json` and the same file is copied into the archive. Compare a run's traffic section with the shape assertions in the journeys: every field the section lists is a field some test read past. The [coverage reference](/docs/observability/coverage) covers the collectors that read other contracts, including OpenAPI documents and GraphQL schemas.

</LearnShell>