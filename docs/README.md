# Documentation site

The ProtoTest documentation site is a Docusaurus project under `docs/`. It is built and deployed separately from the packages. The site documents the current release only. There is no version snapshot to publish.

## Local development

```bash
npm ci
npm run start
```

The dev server opens a browser window and reflects most changes without a restart.

## Checks before a pull request

```bash
npm run typecheck
npm run build
```

`npm run build` writes the static site to `docs/build`. Serve it with any static host.

The full site adds the generated .NET API reference under `/api/`. Run this from the repository root:

```powershell
./proto docs site
```

The script runs the Docusaurus build, builds the API reference with DocFX, and copies it into `docs/build/api`, so the site's `/api` links resolve. Use `-NoRestore` after the DocFX tool has been restored once.

## Deploy

Upload the contents of `docs/build` to Cloudflare Pages. No workflow deploys the site or the API reference: `docs-quality.yml` builds the docs and runs the Lighthouse gate, and `api-reference.yml` builds the reference and uploads it as a CI artifact. Both run for a maintainer to read, not to publish.
