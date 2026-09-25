# Handoff — <stage or work item>

Copy this file to `assets/internal/handoff-<topic>.md` (or paste it into the session's final message)
before stopping mid-plan. A handoff is small and specific; it is written at a stage boundary, not after
an absorbing session.

- Date:
- Repository / branch / commit:
- Plan item: (e.g. `eng/plan-5.md` Phase 2 / audit A2 / `REG-1`)
- Status: (in progress / blocked / done-pending-gates)

## What is committed

One bullet per commit: `<sha> <message>` and whether the working tree is clean.

## Gates evidence

The exact line from `eng/verify.ps1` (or the commands run and their results). If a gate was not run,
say so and why.

## Decisions taken

Anything chosen that the next session must not relitigate, with the reason.

## Open items with the next action

1. The very next concrete action (file, change, test).
2. Remaining items in order.

## Traps discovered

Anything that cost time: a stale doc, a build/feed quirk, a misleading error, an environment
requirement. Include the command that worked.

## Files that matter

Paths a reader must open: the changed files, the tests, the plans/facts updated.
