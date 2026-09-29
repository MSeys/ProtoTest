import prototestPrism from './src/prism/prototest';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

const GITHUB_REPO = 'MSeys/ProtoTest';

/**
 * The star count is baked at build time so readers never call the GitHub API. A slow or failed fetch is not
 * worth failing a build over: the header simply shows the mark without a count.
 */
async function fetchGitHubStars(): Promise<number | null> {
  try {
    const response = await fetch(`https://api.github.com/repos/${GITHUB_REPO}`, {
      headers: {'User-Agent': 'prototest-docs-build'},
      signal: AbortSignal.timeout(3000),
    });
    if (!response.ok) return null;
    const data = (await response.json()) as {stargazers_count?: unknown};
    return typeof data.stargazers_count === 'number' ? data.stargazers_count : null;
  } catch {
    return null;
  }
}

export default async function createConfig(): Promise<Config> {
  const githubStars = await fetchGitHubStars();

  const config: Config = {
  title: 'ProtoTest',
  tagline: 'Test the whole journey. Trace every layer.',
  // Theme-aware: the mark follows the operating system, like the viewer's and the report's.
  favicon: 'img/favicon.svg',

  customFields: {
    githubStars,
  },

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
    announcementBar: {
      id: 'release-1.1',
      content:
        '<span class="announcement-preview">1.1</span> ProtoTest 1.1 is released. <a target="_blank" rel="noopener noreferrer" href="https://www.nuget.org/packages?q=ProtoTest">Install from NuGet</a>',
      isCloseable: true,
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
        {href: 'https://prototest.dev/api/', label: 'API reference', position: 'left'},
        {href: 'https://trace.prototest.dev', label: 'Trace viewer', position: 'left'},
        {to: '/changelog', label: 'Changelog', position: 'left'},
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
      logo: {
        alt: 'ProtoTest',
        src: 'img/brand/prototest-mark-white.svg',
        width: 40,
        height: 40,
        href: '/',
      },
      // Four groups: learning it, looking it up, the project around it, and the community.
      links: [
        {
          title: 'Learn',
          items: [
            {label: 'Learn integration testing', to: '/learn/'},
            {label: 'Installation', to: '/docs/getting-started/installation'},
            {label: 'Your first test', to: '/docs/getting-started/first-test'},
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
            {label: 'Observability', to: '/docs/observability/prototrace'},
            {label: 'Trace viewer', href: 'https://trace.prototest.dev'},
            {label: 'Test runners', to: '/docs/runners/overview'},
            {label: 'Continuous integration', to: '/docs/continuous-integration/'},
            {label: 'Extending', to: '/docs/advanced/extending'},
            {label: 'Agent workflows', to: '/docs/agent-workflows/coding-agents'},
          ],
        },
        {
          title: 'Project',
          items: [
            {label: 'Why ProtoTest', to: '/docs/project/why-prototest'},
            {label: 'ProtoTest compared', to: '/docs/project/compare'},
            {label: 'Benchmarks', to: '/docs/project/benchmarks'},
            {label: 'Roadmap', to: '/docs/project/roadmap'},
            {label: 'FAQ', to: '/docs/project/faq'},
            {label: 'Sustainability', to: '/docs/project/sustainability'},
            {label: 'AI usage', to: '/docs/project/ai-usage'},
            {label: 'Changelog', to: '/changelog'},
          ],
        },
        {
          title: 'Community',
          items: [
            {label: 'GitHub', href: 'https://github.com/MSeys/ProtoTest'},
            {label: 'Discussions', href: 'https://github.com/MSeys/ProtoTest/discussions'},
            {label: 'Issues', href: 'https://github.com/MSeys/ProtoTest/issues'},
            {label: 'Contributing', href: 'https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md'},
            {label: 'Support', href: 'https://github.com/MSeys/ProtoTest/blob/main/SUPPORT.md'},
            {label: 'NuGet', href: 'https://www.nuget.org/packages?q=ProtoTest'},
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
