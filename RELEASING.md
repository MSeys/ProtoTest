# Releasing ProtoTest

Every ProtoTest package ships as one version. The release is: bump the version, roll the changelog,
run the full gate and pack, tag, and let `.github/workflows/release.yml` publish. This file is the
checklist; the scripts it names are the steps.

Release stages run from `version/**` in this cycle. `eng/pack.ps1` refuses to pack the version in
`Directory.Build.targets`'s `PackageValidationBaselineVersion` (the published family), so a branch
that just bumped the version can be packed and consumed before the release.

## 1. Bump the version

Set `<Version>` in `Directory.Build.props` to the release version (`1.1.0`, no pre-release suffix).
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

## 3. Verify and validate

```powershell
pwsh eng/verify.ps1 -Stage release-<version> -Full -Pack
pwsh eng/release.ps1 -Configuration Release -DryRun
```

`-Full` is the CI shape (lint, full test suite, docs) and `-Pack` validates every package against
the baseline: `eng/pack.ps1` packs all 35 packages and checks the shared version, READMEs,
dependency edges, symbol pairs and package validation. `release.ps1 -DryRun` prints the
dependency-ordered push plan from the packed folder without touching NuGet.

## 4. Tag and publish

```powershell
git tag v<version>
git push origin v<version>
```

`release.yml` triggers on `v*` tags. Its verify job runs the CI shape and uploads the packages it
built; the release job publishes exactly those packages with `eng/release.ps1` (NuGet OIDC login
against the `NUGET_USER` secret, or `NUGET_API_KEY` for a local push) and creates the GitHub Release
from the changelog section. `eng/release.ps1` fails when the tag does not match the packed version,
when the folder holds two versions, or when a ProtoTest dependency is not part of the same release.
`workflow_dispatch` runs the same job with `dry_run: true` by default, which packs and plans without
pushing.

## 5. Baseline rollover (after the release publishes)

A published version becomes the compatibility baseline for the next branch, and the packages that
had no baseline now have one. After `x.y.z` is on nuget.org:

1. Set `<PackageValidationBaselineVersion>` in `Directory.Build.targets` to `x.y.z`.
2. Remove `EnablePackageValidation=false` and `PackageValidationOptOutReason` from every package
   that now has a released baseline. Today that is `ProtoTest.Hosting`, `ProtoTest.Devices`,
   `ProtoTest.Devices.WebSocket`, `ProtoTest.Devices.WebSocket.AspNetCore`, `ProtoTest.Web.Pages`,
   `ProtoTest.Traces` and `ProtoTest.Cli`. `eng/pack.ps1` fails a packable project that disables
   package validation without a reason, so the opt-outs cannot silently outlive their rollover.
3. Regenerate the `CompatibilitySuppressions.xml` files against the new baseline
   (`dotnet pack <project> -p:GenerateCompatibilitySuppressionFile=true`) and delete the entries the
   new baseline no longer reports; the old 1.0.1 suppressions describe removals the release already
   shipped.
4. Re-run `pwsh eng/pack.ps1` and `pwsh eng/verify.ps1 -Stage <next-branch> -Full -Pack`.
5. Bump `Directory.Build.props` to the next branch version (`x.(y+1).0-alpha.1`).

## 6. Documentation at release

From `docs/`:

```powershell
npm run docusaurus docs:version <version>
```

This snapshots the current pages as `versioned_docs/version-<version>` and updates `versions.json`.
The announcement bar in `docs/docusaurus.config.ts` points readers at the current release and the
next-version roadmap, so update its `content` for the new release, then run `npm run build` and let
the docs workflow publish the site. The API reference workflow builds from the same source.

## Checklist

- [ ] `Directory.Build.props` carries the release version (not the baseline).
- [ ] `eng/cut-release.ps1` rolled `[Unreleased]` into `## [<version>] - <date>` and left a fresh
      `[Unreleased]`.
- [ ] `node docs/scripts/generate-changelog.mjs` ran and `eng/check-docs.ps1` is green.
- [ ] `eng/verify.ps1 -Stage release-<version> -Full -Pack` is green.
- [ ] `eng/release.ps1 -DryRun` prints the package plan without errors.
- [ ] Tag `v<version>` pushed; `release.yml` published the packages and the GitHub Release.
- [ ] Baseline rollover done; opt-out reasons gone; next branch version bumped.
- [ ] Docs version snapshot and announcement bar updated.
