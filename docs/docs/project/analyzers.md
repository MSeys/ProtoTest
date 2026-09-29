---
sidebar_position: 8
title: Analyzers
description: "ProtoTest.Analyzers: the two context-scoped warnings the framework cannot catch at runtime, and what the package deliberately does not check."
---

---

import CommandBox from '@site/src/components/CommandBox';

# Analyzers

`ProtoTest.Analyzers` is a small Roslyn package for the mistakes that compile and look right but run
outside the lifecycle. It ships two warnings. A rule ships only when a false positive is impossible or trivial to suppress.

<CommandBox title="Add the analyzers" commands={['dotnet add package ProtoTest.Analyzers']} />

The analyzers apply as soon as the package is referenced; add `PrivateAssets="all"` when the package
should not flow to projects that reference your test project.

## The rules

| ID | Severity | What it flags | Runner behavior without it |
| --- | --- | --- | --- |
| `PT0001` | Warning | one method carrying both a ProtoTest test attribute and the runner's own test attribute (`[Test]`, `[Fact]`, `[Theory]`, `[TestMethod]`) | NUnit merges the pair, MSTest fails the test, xUnit.net rejects it with `xUnit1002` |
| `PT0002` | Warning | `Proto.Context` read in a method the runner registers with its own test attribute, without a ProtoTest attribute | The read throws: the context only resolves inside the lifecycle |

`PT0001` flags a doubled test attribute. ProtoTest attributes derive from the runner attribute, so one is enough. NUnit merges the pair. MSTest fails it. xUnit.net rejects it with xUnit1002. `PT0002`
is the plain-test trap: `Proto.Context` only resolves inside the lifecycle, so a
`[Test]`/`[Fact]`/`[TestMethod]` method that reads it - directly or through an accessor such as
`context.Rest()` or `context.Web()` - is guaranteed to throw. The runner pages show the right
attribute for each runner: [NUnit](../runners/nunit.md), [xUnit v2](../runners/xunit.md),
[xUnit v3](../runners/xunit3.md), [MSTest](../runners/mstest.md).

One attribute too many reads like this on the build:

```csharp
[ProtoTest]
[Test] // PT0001: remove the plain attribute, the ProtoTest one already derives from it.
public async Task RestWritesAreVisibleThroughGraphQL()
```

```text
warning PT0001: 'RestWritesAreVisibleThroughGraphQL' carries both [ProtoTest] and [Test]. The ProtoTest attribute already derives from the plain one, so remove [Test].
```

The fix is one deletion:

```csharp
[ProtoTest]
public async Task RestWritesAreVisibleThroughGraphQL()
```

The warnings are suppressible the standard way: `#pragma warning disable PT0001` or
`dotnet_diagnostic.PT0002.severity = none` in `.editorconfig`.

## What it leaves alone

The package does not try to prove that a client or capability is registered. A suite's composition
can live in a referenced assembly and capability declarations are runtime values, so "nothing
registers this" cannot be decided from one compilation; the runtime resolver message stays the
honest answer. It also stays out of style and API-usage rules, reports only plain tests (a plain unit
test next to ProtoTest tests is fine when it does not touch the context), and leaves TUnit alone -
the executor runs every TUnit test inside the lifecycle.

The one-page design, including the rejected rules and their false-positive analysis, is the package
README: [ProtoTest.Analyzers design](https://github.com/MSeys/ProtoTest/blob/main/src/ProtoTest.Analyzers/README.md).
