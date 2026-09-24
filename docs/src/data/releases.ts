/**
 * The homepage release feed. One entry per released version, newest first; `eng/check-docs.ps1`
 * cross-checks the versions and dates against the repository's CHANGELOG.md, so a release that misses
 * this file fails the docs gate instead of leaving the homepage stale.
 */

export interface Release {
  version: string;
  date: string;
  summary: string;
}

export const releases: Release[] = [
  {
    version: '1.0.1',
    date: '2026-09-20',
    summary:
      'Binary response artifacts keep their bytes; the viewer previews .xlsx; the docs gained recipe traces and an API reference.',
  },
  {
    version: '1.0.0',
    date: '2026-09-19',
    summary:
      'The first stable release: one host, execution context and lifecycle; five runner adapters; the full integration catalog; ProtoTrace format 2.0.',
  },
];
