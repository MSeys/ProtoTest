# ProtoTest.Analyzers

> Preview: the surface can change before 1.2.

Roslyn analyzers for ProtoTest suites: the context-scoped mistakes the framework cannot see at
runtime. Two warnings, deliberately narrow - a noisy analyzer is worse than none.

```bash
dotnet add package ProtoTest.Analyzers
```

Analyzers apply automatically once the package is referenced. Add `PrivateAssets="all"` when the
package should not flow to projects that reference your test project.

## The shipped rules

| ID | Title | Severity | Flags |
| --- | --- | --- | --- |
| `PT0001` | ProtoTest test attribute combined with the runner's own test attribute | Warning | one method carrying both a ProtoTest attribute and the runner's plain test attribute |
| `PT0002` | ProtoTest context used in a test without the ProtoTest attribute | Warning | `Proto.Context` read in a method the runner registers with a plain test attribute |

### PT0001 - a method with both registrations

**Pattern.** A method carries a ProtoTest attribute
(`ProtoTest.NUnit.ProtoTestAttribute`, `ProtoTest.MSTest.ProtoTestAttribute`,
`ProtoTest.Xunit.ProtoTestFactAttribute`, `ProtoTest.Xunit.ProtoTestTheoryAttribute`,
`ProtoTest.Xunit3.ProtoTestFactAttribute`, `ProtoTest.Xunit3.ProtoTestTheoryAttribute`) *and* the
runner's plain test attribute (`NUnit.Framework.TestAttribute`, `Xunit.FactAttribute`,
`Xunit.TheoryAttribute`, `Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute`).

**Why it is intent-dependent.** The ProtoTest attributes derive from the runner's plain attributes and
add the lifecycle, so one registration is enough; what the pair does differs per runner, and only the
runner can tell. Today NUnit resolves it to one test *inside* the lifecycle, so the attribute list
simply lies about the intent, while MSTest fails the test at execution ("Only one attribute of type
'TestMethodAttribute' is allowed") and xUnit.net rejects the method at compile time when its analyzers
are referenced (`xUnit1002`). The test compiles and nothing in ProtoTest can warn - the runner decides.

**False-positive analysis.** No runner has a use for the pair, and deleting the plain attribute leaves
the method exactly as intended. Where xunit.analyzers or MSTest.Analyzers are referenced they report
`xUnit1002`/`MSTEST0060` for the same pair; `PT0001` adds the ProtoTest wording and covers suites that
reference the runner's core package without its analyzers (MSTest.TestFramework alone, for example).
Suppress with `#pragma warning disable PT0001` or `dotnet_diagnostic.PT0001.severity = none`.

### PT0002 - Proto.Context outside the lifecycle

**Pattern.** A method carries a plain runner test attribute (`[Test]`, `[Fact]`, `[Theory]`,
`[TestMethod]`), carries no ProtoTest attribute, and reads `ProtoTest.Core.Proto.Context` - directly
or through an accessor such as `Proto.Context.Rest()` or `Proto.Context.Web()`.

**Why it is intent-dependent.** `Proto.Context` resolves from the ambient context that only the
ProtoTest attribute starts; on any other flow it throws `InvalidOperationException`. A plain test
that reads it compiles, runs, and fails mid-test without naming the attribute that is missing.

**False-positive analysis.** The method is registered by the runner itself and has no ProtoTest
attribute, so no context can exist on its flow; the failure is certain. Helpers, attributes and hooks
are not reported on purpose - they run inside a ProtoTest test's flow and are reached from it. TUnit
is excluded on purpose: `ProtoTestExecutor` runs every TUnit `[Test]` inside the lifecycle, so its
`[Test]` is not a plain registration. Suppress with `#pragma warning disable PT0002` or
`dotnet_diagnostic.PT0002.severity = none`.

## Limits

- Client and capability inference are runtime concerns, so no rule flags them; the runtime error names what is missing.

- The analyzers match framework vocabulary by metadata name, never by package reference. A new adapter
  test attribute needs a line in `ProtoTestVocabulary`, pinned by `VocabularyTests`, and a fixture that
  proves the rule fires for it. The fixtures compile against the shipped NUnit and xUnit v3 attributes;
  the MSTest and xUnit v2 assemblies cannot load next to those two, so their fixtures carry the shipped
  metadata names in source stubs (xUnit v2's plain attributes share v3's names).
- `PT0002` proves the missing lifecycle, not a missing client: a ProtoTest test that reads
  `context.Rest()` with nothing registered still fails at runtime with the resolver's message.
- No repository-wide enablement ships in this package; referencing it is the opt-in.
