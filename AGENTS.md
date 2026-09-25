# AGENTS.md — ProtoTest operating contract

ProtoTest is a .NET integration-testing foundation: one host per suite, one execution context per test
(ambient `Proto.Context`), explicitly owned resources, attributes as capabilities and lifecycle, a
`.prototrace` evidence trace plus report sinks, and integration packages that plug into one model.
This file is the operating contract for contributors and coding agents working in this repository.

## Read before you act

1. `eng/plan-5.md` — the plan of record; work only from its next unchecked item.
2. `eng/facts/architecture.md`, `recipes.md`, `gotchas.md` — the model, the way to add things, the
   known traps.
3. `eng/audit-plan-4.md` — open findings; `eng/plan-4.md` — scope and binding decisions (Track W done,
   R/P/X open); `eng/feature-plan.md` — feature scope.
4. `eng/handoff-template.md` — the handoff you write before stopping.

## Non-negotiables

1. **Never create a second mechanism for something the framework owns.** There is one host builder,
   one host, one execution context, one resource registry, one infrastructure registration, one
   capability model, one client resolution, one options registration, one evidence boundary, one
   adapter boundary. Extend the existing one; if that seems impossible, write the finding first.
2. **Use the canonical primitives**: `ProtoOptionsRegistration` for options, `ProtoPolling`/
   `ProtoFlow`/`ProtoReadiness` for waits, `ProtoInfrastructureRegistration` for run pieces,
   `ProtoCapabilityDescriptor` for capabilities, `ProtoTraceEntityKinds`/protocol descriptors for
   vocabulary, `ProtoMetadataRedaction`/`JsonDiagnosticSanitizer` for redaction, `AdapterContract` for
   adapter compliance. `recipes.md` names the canonical example for each kind of change.
3. **Honest capabilities and honest docs.** A capability is declared only by something that can serve
   it; an integration whose address is missing becomes inert and its capability is absent. Never
   advertise what does not exist — not in docs, READMEs, the site or the roadmap.
4. **One behavior change = one test.** Failure paths and parallel safety included; characterization
   first where existing behavior changes deliberately; tests go through the real entry point, not a
   hook in isolation.
5. **Evidence or it did not happen.** A stage ends with `eng/verify.ps1` green (test + lint +
   check-docs, plus pack when packaging changed) and its evidence line recorded in the plan.
6. **Public surface is additive in 1.x.** Accidental-public plumbing may change with a changelog
   entry. A breaking consumer API change needs a plan decision first.
7. **Update the facts with the code.** A lifecycle, registration, capability, address, vocabulary or
   ownership change updates the owning `eng/facts/` file in the same commit. `CHANGELOG.md` and the
   docs Limits sections move with the behavior.
8. **Stop at the stage boundary.** If a stage grows past its checklist, stop, write the handoff, and
   let the next session pick it up. Do not absorb a second stage into one session.

## Workflow

1. Pick the next unchecked item in `eng/plan-5.md` (or an audit stage it references).
2. Check `gotchas.md` for the area; read the canonical example for the recipe.
3. For a deliberate behavior change: pin the current behavior with a test, then change it, then flip
   the test.
4. Run the gates; fix everything before claiming the stage.
5. Commit one stage: code + tests + docs + changelog + facts together, message naming the plan item.
6. Update the plan row with the evidence line. If stopping, write the handoff.

## Gates

| Command | When |
| --- | --- |
| `eng/test.ps1` | every code stage (full suite; use its filters only while iterating) |
| `eng/lint.ps1` | every stage |
| `eng/check-docs.ps1` | docs, README or site changes |
| `eng/pack.ps1` | packaging, csproj, version or new-project changes |
| `eng/verify.ps1 -Stage <name>` | every stage; writes `artifacts/gates/<name>.json` (add `-Pack` when packaging changed) |
| `npm run build` in `docs/` | docs site content or navigation changes |

Do not commit with a red gate, and do not describe a gate as green without the command output.

## Testing rules

- Integration behavior is proved through the host builder and the public API, with a real runner where
  the feature crosses the runner boundary.
- Add the negative paths: missing address, bad configuration, timeout, cancellation, partial failure,
  teardown failure.
- Parallel safety is decided explicitly; link `tests/NUnitParallelization.cs` only when the suite
  isolates its state, and say why in the commit.
- Shared doubles and helpers live in `tests/ProtoTest.TestSupport`; do not copy them into a project.
- Keep `[Category("Characterization")]` on registration-shape and report-markup tests.

## Versioning and feeds

- Branch packages carry `1.1.0-alpha.<n>` once plan-5 Phase 0 lands; never the published version.
- Consumers resolve ProtoTest packages from the local feed produced by `eng/pack.ps1` with
  package-source mapping, and clear `~/.nuget/packages/prototest.*` after a repack.
- `assets/internal/` is gitignored scratch; durable engineering facts live in `eng/facts/`.

## If you are unsure

Search the canonical example first (`recipes.md`), then the audit and plans. If the answer is not in
the repo, ask the user rather than inventing a parallel mechanism — the audits exist because that
invented mechanism is the most expensive failure mode this codebase has.
