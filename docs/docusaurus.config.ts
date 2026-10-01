import prototestPrism from './src/prism/prototest';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

export default async function createConfig(): Promise<Config> {

  const config: Config = {
  title: 'ProtoTest',
  tagline: 'An integration testing foundation for .NET.',
  // Theme-aware: the mark follows the operating system, like the viewer's and the report's.
  favicon: 'img/favicon.svg',

  future: {
    v4: true, // Improve compatibility with the upcoming Docusaurus v4
  },

  url: 'https://prototest.dev',
  baseUrl: '/',

  organizationName: 'MSeys',
  projectName: 'ProtoTest',

  onBrokenLinks: 'throw',
  markdown: {
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },
  themes: [
    '@docusaurus/theme-mermaid',
    [
      // Offline search: the index is built with the site, so it needs no service and no account.
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        indexBlog: false,
        docsRouteBasePath: ['/docs', '/learn'],
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
      },
    ],
  ],

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          routeBasePath: '/docs',
          editUrl: 'https://github.com/MSeys/ProtoTest/tree/main/docs/',
          // Read from git: a page says when it last changed, which matters while the docs move with the code.
          showLastUpdateTime: true,
          // Table cells carry their column heading, so a phone can read a row as a card.
          rehypePlugins: [require('./plugins/rehype-table-labels.cjs')],
          // One set of pages: the site documents the current release only, so `docs/docs` serves `/docs`
          // directly and a release cuts no versioned snapshot.
        },
        blog: false,
        sitemap: {
          ignorePatterns: ['/search'],
        },
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  // The Learn track is a second docs instance: its own content folder, URL and sidebar, so lessons read
  // top to bottom instead of sitting inside the reference tree. The reference stays the only instance
  // under `docs/docs`, so a doc belongs to exactly one sidebar.
  plugins: [
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'learn',
        path: 'learn',
        routeBasePath: 'learn',
        sidebarPath: './sidebars-learn.ts',
        editUrl: 'https://github.com/MSeys/ProtoTest/tree/main/docs/',
        showLastUpdateTime: true,
        rehypePlugins: [require('./plugins/rehype-table-labels.cjs')],
      },
    ],
    [
      '@docusaurus/plugin-client-redirects',
      {
        // The project pages moved under /project. Keep the old routes working for links already shared.
        redirects: [
          {from: '/docs/compare', to: '/docs/project/compare'},
          {from: '/docs/benchmarks', to: '/docs/project/benchmarks'},
          {from: '/docs/faq', to: '/docs/project/faq'},
          {from: '/docs/roadmap', to: '/docs/project/roadmap'},
          // The Learn track was regrouped into tracks; every lesson keeps its old address.
          ...Object.entries(require('./scripts/learn-moves.json') as Record<string, string>).map(([from, to]) => ({from, to})),
        ],
      },
    ],
  ],

  themeConfig: {
    image: 'img/brand/prototest-social.png',
    metadata: [
      {name: 'application-name', content: 'ProtoTest'},
      {property: 'og:site_name', content: 'ProtoTest'},
      {name: 'theme-color', content: '#061a28'},
    ],
    colorMode: {
      defaultMode: 'light',
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'ProtoTest',
      logo: {
        alt: 'ProtoTest',
        src: 'img/brand/prototest-mark.svg',
        srcDark: 'img/brand/prototest-mark-white.svg',
      },
      items: [
        // The navbar holds what a reader reaches for most; the footer repeats every one of these, grouped.
        {
          type: 'docSidebar',
          sidebarId: 'learnSidebar',
          docsPluginId: 'learn',
          position: 'left',
          label: 'Learn',
        },
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Docs',
        },
        {to: '/docs/recipes/overview', label: 'Recipes', position: 'left'},
        {label: 'Resources', position: 'left', items: [
          {label: 'API reference', href: 'https://prototest.dev/api/'},
          {label: 'Trace viewer', href: 'https://trace.prototest.dev'},
          {label: 'Changelog', to: '/changelog'},
        ]},
        {
          type: 'custom-github',
          position: 'right',
        },
        {
          type: 'custom-nuget',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      // Four groups: learning it, looking it up, the project around it, and the community.
      links: [
        {
          title: 'Learn',
          items: [
            {label: 'Learn', to: '/learn/'},
            {label: 'Installation', to: '/docs/getting-started/installation'},
            {label: 'Recipes', to: '/docs/recipes/overview'},
            {label: 'Troubleshooting', to: '/docs/getting-started/troubleshooting'},
          ],
        },
        {
          title: 'Reference',
          items: [
            {label: 'Foundation', to: '/docs/foundation/overview'},
            {label: 'Integrations', to: '/docs/integrations/overview'},
            {label: 'API reference', href: 'https://prototest.dev/api/'},
            {label: 'Trace viewer', href: 'https://trace.prototest.dev'},
          ],
        },
        {
          title: 'Project',
          items: [
            {label: 'Why ProtoTest', to: '/docs/project/why-prototest'},
            {label: 'Benchmarks', to: '/docs/project/benchmarks'},
            {label: 'Roadmap', to: '/docs/project/roadmap'},
            {label: 'Sustainability', to: '/docs/project/sustainability'},
          ],
        },
        {
          title: 'Community',
          items: [
            {label: 'GitHub', href: 'https://github.com/MSeys/ProtoTest'},
            {label: 'Discussions', href: 'https://github.com/MSeys/ProtoTest/discussions'},
            {label: 'Issues', href: 'https://github.com/MSeys/ProtoTest/issues'},
            {label: 'Support', href: 'https://github.com/MSeys/ProtoTest/blob/main/SUPPORT.md'},
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Matthias Seys. Built with Docusaurus.`,
    },
    // Mermaid draws on its neutral base theme; custom.css recolours it from the tokens for both surfaces.
    mermaid: {
      theme: {light: 'base', dark: 'base'},
    },
    prism: {
      theme: prototestPrism,
      darkTheme: prototestPrism,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies Preset.ThemeConfig,
  };

  return config;
}
