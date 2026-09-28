---
sidebar_position: 4
title: Analyzers
description: "ProtoTest.Analyzers: the two context-scoped warnings the framework cannot catch at runtime, and what the package deliberately does not check."
---

# Analyzers

`ProtoTest.Analyzers` is a small Roslyn package for the mistakes that compile and look right but run
outside the lifecycle. It ships two warnings, and a rule only ships when a false positive is
impossible or trivially suppressed - a noisy analyzer is worse than none.

```bash
dotnet add package ProtoTest.Analyzers
```

The analyzers apply as soon as the package is referenced; add `PrivateAssets="all"` when the package
should not flow to projects that reference your test project.

## The rules

| ID | Severity | What it flags |
| --- | --- | --- |
| `PT0001` | Warning | one method carrying both a ProtoTest test attribute and the runner's own test attribute (`[Test]`, `[Fact]`, `[Theory]`, `[TestMethod]`) |
| `PT0002` | Warning | `Proto.Context` read in a method the runner registers with its own test attribute, without a ProtoTest attribute |

`PT0001` is the attribute-doubling trap: the ProtoTest attributes derive from the runner's plain
attribute, so one registration is enough, and what the pair does depends on the runner - NUnit merges
it into one lifecycle test, MSTest fails the test, and xUnit.net rejects it with `xUnit1002`. `PT0002`
is the plain-test trap: `Proto.Context` only resolves inside the lifecycle, so a
`[Test]`/`[Fact]`/`[TestMethod]` method that reads it - directly or through an accessor such as
`context.Rest()` or `context.Web()` - is guaranteed to throw. The runner pages show the right
attribute for each runner: [NUnit](../runners/nunit.md), [xUnit v2](../runners/xunit.md),
[xUnit v3](../runners/xunit3.md), [MSTest](../runners/mstest.md).

The warnings are suppressible the standard way: `#pragma warning disable PT0001` or
`dotnet_diagnostic.PT0002.severity = none` in `.editorconfig`.

## What it deliberately does not check

The package does not try to prove that a client or capability is registered. A suite's composition
can live in a referenced assembly and capability declarations are runtime values, so "nothing
registers this" cannot be decided from one compilation; the runtime resolver message stays the
honest answer. It also stays out of style and API-usage rules, reports only plain tests (a plain unit
test next to ProtoTest tests is fine when it does not touch the context), and leaves TUnit alone -
the executor runs every TUnit test inside the lifecycle.

The one-page design, including the rejected rules and their false-positive analysis, is the package
README: [ProtoTest.Analyzers design](https://github.com/MSeys/ProtoTest/blob/main/src/ProtoTest.Analyzers/README.md).
