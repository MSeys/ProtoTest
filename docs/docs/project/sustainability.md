---
sidebar_position: 6
title: Support and sustainability
description: "Who maintains ProtoTest, how it is funded, what the versioning and deprecation policy is, and what happens if the maintainer stops."
---

# Support and sustainability

A testing foundation is a long-term dependency, and "who keeps this alive?" is a fair question to ask before adopting one. Here are the honest answers.

## Who maintains it

ProtoTest is maintained by one person, [Matthias Seys](https://github.com/MSeys), in personal time. There is no company, no team and no sponsors. That is the same answer as "how is it funded": it is not.

The intent is to grow beyond that. The project is set up so other people can contribute without asking permission. The [contribution guide](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md) covers how to build it, what a feature has to ship with, and how community packages are versioned. Co-maintainers are recruited from people who use it seriously.

In practice support is best-effort with no response-time guarantee. The tests, the docs checks and the review bar hold the line.

## Versioning and deprecation

ProtoTest follows semantic versioning with one documented exception list:

| Change kind | Rule |
| --- | --- |
| Deliberate public API | Breaks only in a major version. Where a replacement exists, the old member is deprecated for at least one released minor before removal, where that is feasible. |
| Accidental-public plumbing | May change in a minor release. Each such change appears under Breaking in the changelog with a migration note. Package validation compares each package with its prior release and fails the pack on an unrecorded break. |
| Patch release | Never breaks behavior. |
| Older lines | Security or critical fixes, decided case by case and never promised. |

A Breaking entry reads like the change it asks for. For example, the entry for the removed REST and GraphQL client registration spells out the replacement: register clients under an application and configure its base URL or endpoints, or use an `AddClient` resolver. The [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md) carries every such entry with its migration note.

All ProtoTest packages share one version and are released together, so one minor release names one coherent set. Packages published outside this repository version independently and declare the ProtoTest they require.

**Fixes** land on the newest released line. If you need a long support window for an old minor, pin it and budget for the upgrade.

## How to get help

- Start with the [documentation](https://prototest.dev/docs/) and the [troubleshooting guide](../getting-started/troubleshooting.md). The error strings there are exact and searchable.
- Questions and open-ended discussion belong in [GitHub Discussions](https://github.com/MSeys/ProtoTest/discussions). Bugs and feature requests go to [GitHub Issues](https://github.com/MSeys/ProtoTest/issues) so they can be tracked to a fix. The [support page](https://github.com/MSeys/ProtoTest/blob/main/SUPPORT.md) says what to include: versions, runner, a minimal reproduction, and the smallest useful excerpt from the trace.
- Suspected vulnerabilities do **not** go in a public issue. Follow [SECURITY.md](https://github.com/MSeys/ProtoTest/blob/main/SECURITY.md) instead.

## If the maintainer stops

This is the question in-house frameworks fail, and the project is designed around it:

- The license is [MIT](https://github.com/MSeys/ProtoTest/blob/main/LICENSE). The source can be forked, vendored or continued by anyone, with no permission and no relicensing risk.
- The [trace format is documented and versioned](../observability/prototrace.md), so the evidence a suite produces stays readable even if the tooling around it is replaced.
- The [extension points](../advanced/extending.md) are public, so new integrations can be written without changing the core.
- There is no hosted service, account system or license server that could be switched off. The packages are local and the viewer is a static page.

None of that guarantees continued maintenance. It is the difference between a framework that can be taken over and one that merely stops. The [same argument](./compare.md#building-the-foundation-in-house) makes adopting a public foundation safer than building your own.

## The honest caveats

ProtoTest is young, and the trade-offs of a solo project are real. Reviews can take time, the ecosystem is small, and answers on the internet are rare compared to older libraries. If any of that is disqualifying for your team, the [comparison page](./compare.md) lists good alternatives. Choosing them is a legitimate outcome, not a failure of this page.
