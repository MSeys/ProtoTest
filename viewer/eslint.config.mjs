// Bug-finding rules only: formatting stays with the editor config, and style stays with review.
import tseslint from 'typescript-eslint';
import vue from 'eslint-plugin-vue';
import vueParser from 'vue-eslint-parser';

export default [
  {ignores: ['dist/**', 'node_modules/**', '.perf/**', '**/*.d.ts']},
  ...vue.configs['flat/essential'],
  {
    files: ['src/**/*.{ts,vue}'],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
        extraFileExtensions: ['.vue'],
      },
    },
    plugins: {'@typescript-eslint': tseslint.plugin},
    rules: {
      '@typescript-eslint/no-floating-promises': 'error',
      '@typescript-eslint/no-misused-promises': 'error',
      '@typescript-eslint/no-unused-vars': ['error', {argsIgnorePattern: '^_', varsIgnorePattern: '^_'}],
      // Single-word names (Icon, Panel, Tabs) are the viewer's convention for its own UI parts.
      'vue/multi-word-component-names': 'off',
    },
  },
];
