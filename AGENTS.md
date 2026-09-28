# AGENTS.md — ProtoTest operating contract

ProtoTest is a .NET integration-testing foundation: one host per suite, one execution context per test
(ambient `Proto.Context`), explicitly owned resources, attributes as capabilities and lifecycle, a
`.prototrace` evidence trace plus report sinks, and integration packages that plug into one model.
This file is the operating contract for contributors and coding agents working in this repository.

## Read before you act

1. `assets/internal/records/plan-6.md` — the plan of record for the consolidation phases (C0–C4); its
   final phase hands back to `plan-5.md`/`plan-4.md` for features. Work only from its next unchecked
   item. The process records — plans, audits, handoffs, reviews — live in the private `records`
   checkout at `assets/internal/records/` (a separate git repository); a contributor without that
   checkout uses the public docs and `CONTRIBUTING.md`.
2. `assets/internal/records/facts/architecture.md`, `recipes.md`, `gotchas.md` — the model, the way to
   add things, the known traps; `assets/internal/records/dx-review.md` — the API/DX consistency
   register (public surface follows it, or the register is updated with the reason).
3. `assets/internal/records/audit-plan-4.md` — the closed audit; `audit-plan-5.md` — the open one when
   it exists; `plan-5.md`/`plan-4.md` — scope and binding decisions; `feature-plan.md` — feature scope.
4. `assets/internal/records/handoff-template.md` — the handoff you write before stopping.

## Non-negotiables

1. **Never create a second mechanism for something the framework owns.** There is one host builder,
   one host, one execution context, one resource registry, one infrastructure registration, one
   capability model, one client resolution, one options registration, one evidence boundary, one
   adapter boundary. Extend the existing one; if that seems impossible, write the finding first.
2. **Use the canonical primitives**: `ProtoOptionsRegistration` for options, `ProtoPolling`/
   `ProtoFlow`/`ProtoReadiness` for waits, `ProtoInfrastructureRegistration` for run pieces,
   `ProtoCapabilityDescriptor` for capabilities, `ProtoTraceEntityKinds`/protocol descriptors for
   vocabulary, `ProtoMetadataRedaction`/`JsonDiagnosticSanitizer` for redaction, `AdapterContract` for
   adapter compliance. `facts/recipes.md` names the canonical example for each kind of change.
3. **Honest capabilities and honest docs.** A capability is declared only by something that can serve
   it; an integration whose address is missing becomes inert and its capability is absent. Never
   advertise what does not exist — not in docs, READMEs, the site or the roadmap.
4. **One behavior change = one test.** Failure paths and parallel safety included; characterization
   first where existing behavior changes deliberately; tests go through the real entry point, not a
   hook in isolation.
5. **Evidence or it did not happen.** A stage ends with `eng/verify.ps1` green (test + lint +
   check-docs, plus pack when packaging changed) and its evidence line recorded in the plan.
6. **The published surface is additive in 1.x.** Additive and obsolete shims protect what consumers
   already have - the released packages. An API that only exists on the current branch is fixed
   cleanly before its first release: no facades, no obsolete aliases, no deprecation theatre for
   unreleased surface, with a changelog entry recording the change. Accidental-public plumbing may
   change with a changelog entry. A breaking change to the *published* surface needs a plan decision
   first.
7. **Update the facts with the code.** A lifecycle, registration, capability, address, vocabulary or
   ownership change updates the owning `facts/` file in the records checkout, in the same commit.
   `CHANGELOG.md` and the docs Limits sections move with the behavior.
8. **Stop at the stage boundary.** If a stage grows past its checklist, stop, write the handoff, and
   let the next session pick it up. Do not absorb a second stage into one session.
9. **Comments are short, direct, and explain the code as it stands.** No audit IDs, plan items or
   record-file references in source comments; the `facts/` files in the private records checkout carry
   the history. One or two sentences of why, not a walkthrough; no em-dashes; no essay tone. READMEs
   follow the same voice: short, direct sentences that say what the package does and link to the docs
   for depth. The docs and the learning track carry the detail.

## Workflow

1. Pick the next unchecked item in the records plan of record (`assets/internal/records/plan-6.md`;
   after C4, `plan-5.md` Phase 4).
2. Check `facts/gotchas.md` for the area; read the canonical example for the recipe.
3. For a deliberate behavior change: pin the current behavior with a test, then change it, then flip
   the test.
4. Run the gates; fix everything before claiming the stage.
5. Commit one stage: code + tests + docs + changelog + facts together. The title is
   `Feature - <Part> - <short description>` or `Bug - <Part> - <short description>` (a docs-only or
   tooling stage uses `Docs` or `Tooling`); a 1-3 line body names the plan item and the evidence.
   Changelog entries follow the same shape (`RELEASING.md`).
6. Update the plan row with the evidence line. If stopping, write the handoff.

## Orchestrated sessions (worker cycle)

When a controller session runs workers:

1. Dispatch one bounded stage per worker, with the handoff, the stage ID, the acceptance criteria and
   the gate command.
2. A worker never commits and never starts a second stage; a worker that cannot finish writes the
   handoff from `assets/internal/records/handoff-template.md` and stops.
3. The controller verifies the worker's gate line and diff, then runs the consistency pass: naming and
   API shape against `facts/recipes.md` and `assets/internal/records/dx-review.md` (a public surface
   follows the recorded idiom, or the register is updated with the reason), and facts/plan/changelog
   updates in the same commit.
4. A stage that changes the public surface also gets an independent review worker before the controller
   commits. The reviewer verifies the evidence record and the diff and re-runs the affected test
   projects; it re-runs the full gate (`-Full`, add `-Pack` when packaging changed) when the evidence is
   missing, red, or the change touches shared lifecycle/registration/evidence boundaries. Phase 1's
   review is the template: verdict, findings table with evidence, proposed facts lines, and what could
   not be verified.

## Gates

| Command | When |
| --- | --- |
| `eng/test.ps1` | every code stage (full suite; use its filters only while iterating) |
| `eng/lint.ps1` | every stage |
| `eng/check-docs.ps1` | docs, README or site changes |
| `eng/pack.ps1` | packaging, csproj, version or new-project changes |
| `eng/verify.ps1 -Stage <name>` | every stage; scopes to the working-tree change, or the HEAD commit on a clean tree; a docs-only stage records `docs-only`, a stage that only touched gate scripts/workflows records `tooling` and runs the gate fixtures, and a code stage whose lint/tests were skipped records `incomplete` (non-green) unless `-AllowSkippedCodeGates` names the exception; `-Pack` when public surface or packaging changed, `-Full` to force the CI shape; writes `artifacts/gates/<name>.json` |
| `eng/test-gates.ps1` | the gate scripts' own fixtures; runs inside `verify.ps1` when `eng/**.ps1` or workflows changed, and in CI |
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
- `assets/internal/` is gitignored scratch; its `records/` subdirectory is a private git repository
  holding the process records (plan of record, audits, handoffs, reviews) and the engineering facts
  the code evolves with (`facts/`).

## If you are unsure

Search the canonical example first (`facts/recipes.md`), then the audit and plans. If the answer is not in
the repo, ask the user rather than inventing a parallel mechanism — the audits exist because that
invented mechanism is the most expensive failure mode this codebase has.
