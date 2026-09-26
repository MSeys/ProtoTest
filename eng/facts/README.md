# ProtoTest engineering facts

This directory is the tracked, current source of truth for **how ProtoTest works internally** — the
model, the recipes, and the traps. It exists so sessions stop re-deriving the framework, copying
neighboring code, or re-inventing a mechanism that already exists.

## Read in this order

1. `architecture.md` — the one model: lifecycle, ownership, registration semantics, capabilities,
   addresses, evidence, vocabulary, the inventory of canonical types, invariants.
2. `recipes.md` — how to add each kind of thing, and the anti-patterns the audits found.
3. `gotchas.md` — verified traps with the action to take.
4. The plan of record, audits, handoffs and reviews live in the private `records` checkout
   (`assets/internal/records/` when it is present); the public `CONTRIBUTING.md` and the docs are the
   fallback when it is not.

## Rules

- **Source wins.** If this directory and the code disagree, the code is right and this directory is the
  bug: fix it in the same commit as the code change.
- **Every model change updates the owning fact in the same commit.** A new lifecycle, registration
  semantic, capability rule, address source, vocabulary kind or ownership rule is not done until
  `architecture.md` says so.
- **A behavior change updates `gotchas.md`** when it fixes or introduces a trap.
- **A new recipe** is added when a genuinely new kind of extension point appears; a new provider or
  manager alone is not a new kind.
- Keep entries short, concrete and dated only where the audit trail matters; the audit and plan files
  carry the history.

## Relationship to `assets/internal/`

`assets/internal/` is gitignored local scratch (`.gitignore:488`). Its `docs-facts/` set was written at
`db9d7aa` against `0.1.0-alpha` for the documentation rework: useful as an archive, never as guidance.
The engineering facts that must survive sessions live here, because this directory is committed.

## Freshness

Every fact file names the code state it was verified at. When a stage ends, update the marker in the
files it touched (or note the verified HEAD). The docs gate already keeps documentation from teaching
dead symbols; a similar check for the fact marker is welcome when it can be done without ceremony.
