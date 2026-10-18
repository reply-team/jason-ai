import js from '@eslint/js';
import tseslint from 'typescript-eslint';

const noMocking = ['msw', 'nock', 'fetch-mock', 'axios'];

export default tseslint.config(
  { ignores: ['dist/**', 'node_modules/**', 'artifacts/**', 'playwright-report/**', 'test-results/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    rules: {
      'no-restricted-imports': ['error', { paths: noMocking }],
    },
  },
  {
    // The packaging scripts run under Node, outside the browser's globals.
    files: ['scripts/**/*.mjs'],
    languageOptions: { globals: { Buffer: 'readonly', process: 'readonly', console: 'readonly' } },
  },
  {
    // Guard 1: a unit test tests a pure module. No components, no DOM, no test renderer.
    files: ['src/**/*.test.ts', 'tests/guards/**/*.test.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: noMocking,
          patterns: [
            {
              group: ['preact', 'preact/*', '@preact/*', '@testing-library/*'],
              message: 'A unit test tests a pure module: no components, no DOM.',
            },
          ],
        },
      ],
    },
  },
  {
    // Guard 5: only the harness starts processes; a spec that wants `jason campaign create` cannot have it.
    files: ['tests/e2e/*.spec.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            ...noMocking,
            { name: 'node:child_process', message: 'Only tests/e2e/harness.ts starts processes.' },
            { name: 'child_process', message: 'Only tests/e2e/harness.ts starts processes.' },
          ],
        },
      ],
    },
  },
);
