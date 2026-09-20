module.exports = {
  ci: {
    collect: {
      staticDistDir: './build',
      url: [
        'http://localhost/',
        'http://localhost/docs/',
        'http://localhost/docs/integrations/overview',
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
