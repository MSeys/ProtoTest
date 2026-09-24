---
sidebar_position: 3
title: Support and sustainability
description: "Who maintains ProtoTest, how it is funded, what the versioning and deprecation policy is, and what happens if the maintainer stops."
---

# Support and sustainability

A testing foundation is a long-term dependency, and "who keeps this alive?" is a fair question to ask before adopting one. Here are the honest answers.

## Who maintains it

ProtoTest is maintained by one person, [Matthias Seys](https://github.com/MSeys), in personal time. There is no company, no team and no sponsors. That is the same answer as "how is it funded": it is not.

The intent is to grow beyond that. The project is set up so other people can contribute without asking permission - the [contribution guide](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md) covers how to build it, what a feature has to ship with, and how community packages are versioned. Co-maintainers are recruited from people who use it seriously.

What that means in practice: support is **best-effort**, with no response-time guarantee, and the maintainer's own standards - the tests, the docs checks, the review bar - are the ones holding the line in the meantime.

## Versioning and deprecation

ProtoTest follows semantic versioning with one documented exception list, because that is what the project can honestly promise:

- **Deliberate public APIs** break only in major versions. Where a replacement exists, the old member is deprecated with an `Obsolete` attribute (or an equivalent documented notice) for at least one released minor before it is removed, where that is feasible.
- **Accidental-public plumbing and internal implementation types** - types that were never meant to be constructed or implemented outside the framework - may change in a minor release. Every such change is listed under **Breaking** in the [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md) with a migration note, and the build gates it: package validation compares each package against its previous release, so an unintentional break fails the pack step and the deliberate ones are recorded explicitly.
- **Patch releases** do not break behavior.

All ProtoTest packages share one version and are released together, so "ProtoTest 1.2" names one coherent set. Packages published outside this repository version independently and declare the ProtoTest they require.

**Fixes** land on the newest released line. Older lines receive security or critical fixes when they matter, decided case by case and never promised. If you need a long support window for an old minor, pin it and budget for the upgrade.

## How to get help

- Start with the [documentation](https://prototest.dev/docs/) and the [troubleshooting guide](../getting-started/troubleshooting.md); the error strings there are exact and searchable.
- Questions, bugs, and feature ideas go to [GitHub Issues](https://github.com/MSeys/ProtoTest/issues). The [support page](https://github.com/MSeys/ProtoTest/blob/main/SUPPORT.md) says what to include - versions, runner, a minimal reproduction, and the smallest useful excerpt from the trace.
- Suspected vulnerabilities do **not** go in a public issue; follow [SECURITY.md](https://github.com/MSeys/ProtoTest/blob/main/SECURITY.md) instead.

## If the maintainer stops

This is the question in-house frameworks fail, and the project is designed around it:

- The license is [MIT](https://github.com/MSeys/ProtoTest/blob/main/LICENSE). The source can be forked, vendored or continued by anyone, with no permission and no relicensing risk.
- The [trace format is documented and versioned](../observability/prototrace.md), so the evidence a suite produces stays readable even if the tooling around it is replaced.
- The [extension points](../advanced/extending.md) are public, so new integrations can be written without changing the core.
- There is no hosted service, account system or license server that could be switched off; the packages are local and the viewer is a static page.

None of that is a guarantee of continued maintenance - nothing in open source is. It is the difference between a framework that can be taken over and one that merely stops: the [same argument](../compare.md#building-the-foundation-in-house) that makes adopting a public foundation safer than building your own.

## The honest caveats

ProtoTest is young, and the trade-offs of a solo project are real: reviews can take time, the ecosystem is small, and answers on the internet are rare compared to older libraries. If any of that is disqualifying for your team, the [comparison page](../compare.md) lists good alternatives - choosing them is a legitimate outcome, not a failure of this page.
