// Bug-finding rules only: formatting stays with the editor config, and style stays with review.
import tseslint from 'typescript-eslint';

export default [
  {ignores: ['build/**', '.docusaurus/**', 'node_modules/**', '**/*.generated.ts', '**/*.d.ts']},
  {
    files: ['src/**/*.{ts,tsx}'],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: {projectService: true, tsconfigRootDir: import.meta.dirname},
    },
    plugins: {'@typescript-eslint': tseslint.plugin},
    rules: {
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-misused-promises': 'error',
      '@typescript-eslint/no-unused-vars': ['error', {argsIgnorePattern: '^_', varsIgnorePattern: '^_'}],
    },
  },
];
