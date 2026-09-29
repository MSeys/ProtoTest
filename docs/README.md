# Documentation site

The ProtoTest documentation site is a Docusaurus project under `docs/`. It is built and deployed
separately from the packages.

## Local development

```bash
npm install
npm run start
```

The dev server opens a browser window and reflects most changes without a restart.

## Build

```bash
npm run build
```

This writes the static site to `docs/build`; serve it with any static host.

The full site adds the generated .NET API reference under `/api/`. Run this from the repository root:

```powershell
./eng/build-docs-site.ps1
```

The script runs the Docusaurus build, builds the API reference with DocFX, and copies it into
`docs/build/api`, so the site's `/api` links resolve. Use `-NoRestore` after the DocFX tool has been
restored once.

## Deploy

Upload the contents of `docs/build` to Cloudflare Pages. The site documents the current release only,
so there is no version snapshot to publish. No workflow deploys the site or the API reference:
`docs-quality.yml` builds the docs and runs the Lighthouse gate, and `api-reference.yml` builds the
reference and uploads it as a CI artifact. Both run for a maintainer to read, not to publish.
