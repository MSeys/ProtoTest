# Releasing ProtoTest

Every ProtoTest package ships as one version. The release is: bump the version, roll the changelog,
run the full gate and pack, tag, and let `.github/workflows/release.yml` publish. This file is the
checklist; the scripts it names are the steps.

Release stages run from `version/**` in this cycle. `eng/pack.ps1` refuses to pack the version in
`Directory.Build.targets`'s `PackageValidationBaselineVersion` (the published family), so a branch
that just bumped the version can be packed and consumed before the release.

## 1. Bump the version

Set `<Version>` in `Directory.Build.props` to the release version (for example `1.2.0`, no pre-release suffix).
Branch builds use `x.y.0-alpha.<n>`; the release version must be new (see the pack guard above).

## 2. Roll the changelog

`CHANGELOG.md` is Keep a Changelog: the release's entries live under `## [Unreleased]`. Run:

```powershell
pwsh eng/cut-release.ps1
```

It renames the section to `## [<version>] - <yyyy-MM-dd>` using `<Version>` from
`Directory.Build.props`, inserts a fresh empty `## [Unreleased]` above it, and fails when
`[Unreleased]` has no content, when there is no `[Unreleased]` heading, or when the version already
has a section. It only edits the changelog; the version bump is step 1.

Then regenerate the documentation feed:

```powershell
node docs/scripts/generate-changelog.mjs
```

`eng/check-docs.ps1` fails when `docs/src/pages/changelog.md` or
`docs/src/data/changelog.generated.ts` is stale. The release workflow reads the `## [<version>]`
section for the GitHub Release notes and fails when it is missing; it never falls back to generated
notes, because a release nobody wrote down is the failure this step prevents.

### Changelog conventions

A release section uses `### Features`, `### Fixes` and - only when the release has any -
`### Breaking changes`. Entries are one line, `- <Area>: <short title>.`, mirroring the commit title
(`Feature - <Part> - <short>` / `Bug - <Part> - <short>`); `<Area>` is the package family or topic
(`Core`, `REST`, `GraphQL`, `Messaging`, `Web`, `Docs`). A change that needs a migration note gets at
most one sub-bullet; everything else links the docs page. Never-public churn (alpha-only fixes,
internal refactors, gate tooling) does not get its own entry - fold it into the public feature or fix
that needed it. Write every release section this way from the start.

## 3. Verify and validate

```powershell
pwsh eng/verify.ps1 -Stage release-<version> -Full -Pack
pwsh eng/release.ps1 -Configuration Release -DryRun
```

`-Full` is the CI shape (lint, full test suite, docs) and `-Pack` validates every package against
the baseline: `eng/pack.ps1` packs all 44 packages and checks the shared version, READMEs,
dependency edges, symbol pairs and package validation. `release.ps1 -DryRun` prints the
dependency-ordered push plan from the packed folder without touching NuGet.

## 4. Tag and publish

```powershell
git tag v<version>
git push origin v<version>
```

`release.yml` triggers on `v*` tags. Its verify job runs the CI shape and uploads the packages it
built; the release job publishes exactly those packages with `eng/release.ps1` (NuGet OIDC login
against the `NUGET_USER` secret, or `NUGET_API_KEY` with `-AllowBranch` for a deliberate local push)
and creates the GitHub Release from the changelog section. Publishing is tag-gated: the workflow logs
in and publishes only from a `v*` tag, a dispatch with `dry_run: false` from a branch fails at the
guard instead of pushing, and `eng/release.ps1` refuses a real push from any non-tag ref (or a local
run with no CI ref) unless `-AllowBranch` is passed. `eng/release.ps1` fails when the tag does not
match the packed version, when the folder holds two versions, or when a ProtoTest dependency is not
part of the same release. `workflow_dispatch` validates the plan without pushing (`dry_run: true`,
the default).

## 5. Baseline rollover (after the release publishes)

A published version becomes the compatibility baseline for the next branch, and the packages that
had no baseline now have one. After `x.y.z` is on nuget.org:

1. Set `<PackageValidationBaselineVersion>` in `Directory.Build.targets` to `x.y.z`.
2. Remove `EnablePackageValidation=false` and `PackageValidationOptOutReason` from every package
   that now has a released baseline. Today that is 17 packages: `ProtoTest.Hosting`,
   `ProtoTest.Devices`, `ProtoTest.Devices.Mqtt`, `ProtoTest.Devices.Mqtt.Testcontainers`,
   `ProtoTest.Devices.WebSocket`, `ProtoTest.Devices.WebSocket.AspNetCore`, `ProtoTest.Web.Pages`,
   `ProtoTest.Traces`, `ProtoTest.Cli`, `ProtoTest.Analyzers`, `ProtoTest.Aspire`,
   `ProtoTest.Diagnosis`, `ProtoTest.Feedback`, `ProtoTest.Mcp`, `ProtoTest.Messaging.MassTransit`,
   `ProtoTest.Verification` and `ProtoTest.WireMock`. `eng/pack.ps1` fails a packable project that
   disables package validation without a reason, so the opt-outs cannot silently outlive their
   rollover.
3. Regenerate the `CompatibilitySuppressions.xml` files against the new baseline
   (`dotnet pack <project> -p:GenerateCompatibilitySuppressionFile=true`) and delete the entries the
   new baseline no longer reports; the old 1.0.1 suppressions describe removals the release already
   shipped.
4. Re-run `pwsh eng/pack.ps1` and `pwsh eng/verify.ps1 -Stage <next-branch> -Full -Pack`.
5. Bump `Directory.Build.props` to the next branch version (`x.(y+1).0-alpha.1`).

## 6. Documentation at release

The site documents the current release only: no versioned snapshot is cut, so there is no
`docs:version` step and no `versions.json` to update. Deploy the site **after** the packages publish
(step 4), because the site's release wording describes the published packages.

Bring the public default branch to the released commit first, so the docs links that target `main`
(the skills bundle, the sample journeys, the Analyzers README) resolve. Then:

1. Update the announcement bar's `content` in `docs/docusaurus.config.ts` for the new release.
2. Build the complete site from the repository root:

   ```powershell
   pwsh eng/build-docs-site.ps1
   ```

   The script runs `npm run build` in `docs/`, builds the API reference with
   `eng/build-api-reference.ps1`, and copies it into `docs/build/api`, so the site's `/api` links
   resolve.
3. Upload the contents of `docs/build` (Docusaurus plus `/api`) to Cloudflare Pages.

No workflow publishes the site or the API reference. `docs-quality.yml` builds the docs and runs the
Lighthouse gate on pull requests, and `api-reference.yml` builds the reference and uploads it as a CI
artifact; its push trigger runs on `main` only. Both are checks for a maintainer to read, not deploys.

## Checklist

- [ ] `Directory.Build.props` carries the release version (not the baseline).
- [ ] `eng/cut-release.ps1` rolled `[Unreleased]` into `## [<version>] - <date>` and left a fresh
      `[Unreleased]`.
- [ ] `node docs/scripts/generate-changelog.mjs` ran and `eng/check-docs.ps1` is green.
- [ ] `eng/verify.ps1 -Stage release-<version> -Full -Pack` is green.
- [ ] `eng/release.ps1 -DryRun` prints the package plan without errors.
- [ ] Tag `v<version>` pushed; `release.yml` published the packages and the GitHub Release.
- [ ] Public default branch brought to the released commit; the `main` links resolve.
- [ ] Baseline rollover done; opt-out reasons gone; next branch version bumped.
- [ ] Announcement bar updated; `eng/build-docs-site.ps1` built `docs/build` with `/api`; the folder uploaded to Cloudflare Pages.
