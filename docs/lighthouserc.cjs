module.exports = {
  ci: {
    collect: {
      staticDistDir: './build',
      url: [
        'http://localhost/',
        'http://localhost/docs/',
        'http://localhost/docs/benchmarks',
        'http://localhost/docs/compare',
        'http://localhost/docs/faq',
        'http://localhost/docs/foundation/concurrency',
        'http://localhost/docs/foundation/time',
        'http://localhost/docs/integrations/devices',
        'http://localhost/docs/integrations/hosting',
        'http://localhost/docs/integrations/overview',
        'http://localhost/docs/project/ai-usage',
        'http://localhost/docs/project/sustainability',
        'http://localhost/docs/project/why-prototest',
        'http://localhost/docs/recipes/overview',
        'http://localhost/docs/roadmap',
      ],
      numberOfRuns: 1,
    },
    assert: {
      assertions: {
        'categories:accessibility': ['error', {minScore: 0.9}],
        'categories:seo': ['error', {minScore: 0.9}],
        'categories:best-practices': ['warn', {minScore: 0.9}],
        'categories:performance': ['warn', {minScore: 0.8}],
      },
    },
    upload: {
      target: 'filesystem',
      outputDir: './.lighthouseci/reports',
    },
  },
};
