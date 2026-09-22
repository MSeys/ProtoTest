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
        docsRouteBasePath: '/docs',
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
      id: 'release-1.0',
      content:
        '<span class="announcement-preview">1.0</span> ProtoTest 1.0 is released: packages are stable on NuGet and this documentation matches the release. &nbsp; <a target="_blank" rel="noopener noreferrer" href="https://www.nuget.org/packages?q=ProtoTest">Install from NuGet</a>',
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
      // Three groups: learning it, looking something up, and the project around it.
      links: [
        {
          title: 'Learn',
          items: [
            {label: 'Installation', to: '/docs/getting-started/installation'},
            {label: 'Your first test', to: '/docs/getting-started/first-test'},
            {label: 'Recipes', to: '/docs/recipes/overview'},
            {label: 'CI', to: '/docs/continuous-integration/'},
            {label: 'Troubleshooting', to: '/docs/getting-started/troubleshooting'},
          ],
        },
        {
          title: 'Reference',
          items: [
            {label: 'Foundation', to: '/docs/foundation/overview'},
            {label: 'API reference', href: 'https://prototest.dev/api/'},
            {label: 'Integrations', to: '/docs/integrations/overview'},
            {label: 'Observability', to: '/docs/observability/prototrace'},
            {label: 'Test runners', to: '/docs/runners/overview'},
            {label: 'Continuous integration', to: '/docs/continuous-integration/'},
            {label: 'Extending', to: '/docs/advanced/extending'},
          ],
        },
        {
          title: 'Project',
          items: [
            {label: 'Trace viewer', href: 'https://trace.prototest.dev'},
            {label: 'Why ProtoTest', to: '/docs/project/why-prototest'},
            {label: 'AI usage', to: '/docs/project/ai-usage'},
            {label: 'Changelog', to: '/changelog'},
            {label: 'GitHub', href: 'https://github.com/MSeys/ProtoTest'},
            {label: 'Contributing', href: 'https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md'},
            {label: 'Support', href: 'https://github.com/MSeys/ProtoTest/blob/main/SUPPORT.md'},
            {label: 'NuGet', href: 'https://www.nuget.org/packages?q=ProtoTest'},
            {label: 'Issues', href: 'https://github.com/MSeys/ProtoTest/issues'},
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
