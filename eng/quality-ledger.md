# ProtoTest quality ledger

One page to judge movement, not the newest findings list. Update it when an audit closes or a release
gate turns; do not add per-finding noise. Counts are the severity labels in that audit's own register.

## Audit trend

| Audit | Date | Baseline | Findings | Critical | High | Medium-high / medium | Low-medium / low / obs | Critical-or-high in code that predates the audit | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 — cleanup | 2026-09-23 | `500daf9` | not severity-scored | — | — | — | — | — | Delivered 12 stages; headline fixes: MSTest result masking, Selenium thread affinity, quadratic report flattening, Sheets host leak |
| 2 — convergence | 2026-09-24 | `fc0afa2` | 21 | 0 | 3 | 9 (incl. F17 medium-high) | 9 | 3 (F1–F3: Core web semantics, client init once per chain, per-axis redaction) | Foundation migrations (RabbitMQ 7.x, OpenApi 3.x) and one outcome/evidence policy |
| 3 — boundaries | 2026-09-24 | `09d2590` | 51 | 0 | 5 | 18 | 28 (incl. 1 observation) | 5 (A1 raw finding metadata, B1 release re-arm, C1 listener ownership, D1 split client entity, E1 NUnit boundary) | Runner boundaries, evidence boundary, outcome vocabulary; plan closed |
| 4 — post-feature-push | 2026-09-25 | `61220f5` | 34 | 2 | 8 | 14 | 12 | 0 | The criticals are in the new reference repo (fixed tenant, eager broker read). The highs sit in the P1–P4f seams (conditional capability, worker `Main`, in-process device routing) and release evidence; no finding in pre-push Core |

Reading: the classes the audits keep finding have shifted from "Core model wrong" (Audit 1–3) to "new
surface built faster than its contracts" (Audit 4). Kernel criticals must stay at 0. If the next audit
finds critical/high in code that predates the previous audit *again*, the class is not converging and
its design needs a deliberate decision, not another fix.

## Release and demo gates

| Gate | State | Evidence |
| --- | --- | --- |
| Full suite + lint + docs green on `main` | last release (1.0.1) | CI `main` run |
| Branch verified (`version/1.1`) | green (P0a/P0b) | `verify P0-guardrails` (lint/docs/test/pack PASS at `1.1.0-alpha.1`, `4bafa6d`), `verify A0-core`, `verify A0-devices`; per-stage files in `artifacts/gates/` |
| Distinct branch version (`1.1.0-alpha.<n>`) | green (`4bafa6d`) | `Directory.Build.props`; pack guard refuses the published baseline |
| Reference suite green, container mode | red (first journey never green) | plan-5 Phase 1; date recorded here |
| Reference suite green, configured mode, no code change | red | plan-5 Phase 1 |
| `COVERAGE.md` honest and linked | missing | plan-5 Phase 1 |
| Docs versioned at 1.1 (`docs:version`) | not cut | plan-5 Phase 4 / release checklist |
| Package validation rollover (post-1.0.1 opt-outs) | pending | audit TST-5 / Stage A7 |

## What "good enough" means

- 0 critical and 0 high findings in code older than the previous audit; new-surface findings fixed or
  carrying a recorded decision.
- Every plan row backed by an evidence file; no prose greens.
- The reference suite green in two modes with no code change, with a real coverage gap.
- No user-facing artifact teaches a symbol or a mode that does not exist.
- One audit per release cycle, not continuous; the fact base (`eng/facts/`) makes findings arrive from
  CI and review instead of from archaeology.

## Update protocol

- Append one row per audit as it closes; never edit history.
- Turn `pending` to `green`/`red` with the date and the evidence path/commit.
- If a gate turns red after being green, record the date and the reason once; the fix belongs to the
  plan, not the ledger.
