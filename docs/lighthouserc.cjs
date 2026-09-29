module.exports = {
  ci: {
    collect: {
      staticDistDir: './build',
      url: [
        'http://localhost/',
        'http://localhost/docs/',
        'http://localhost/docs/foundation/concurrency',
        'http://localhost/docs/foundation/time',
        'http://localhost/docs/integrations/devices',
        'http://localhost/docs/integrations/hosting',
        'http://localhost/docs/integrations/overview',
        'http://localhost/docs/project/ai-usage',
        'http://localhost/docs/project/benchmarks',
        'http://localhost/docs/project/compare',
        'http://localhost/docs/project/faq',
        'http://localhost/docs/project/roadmap',
        'http://localhost/docs/project/sustainability',
        'http://localhost/docs/project/why-prototest',
        'http://localhost/docs/recipes/overview',
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
