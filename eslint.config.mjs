import js from '@eslint/js';
import globals from 'globals';

export default [
  {
    ignores: ['**/node_modules/**', '**/bin/**', '**/obj/**', '**/target/**', '**/.venv/**', '**/TestResults/**'],
  },

  js.configs.recommended,

  {
    rules: {
      eqeqeq: 'error',
      'no-var': 'error',
      'prefer-const': 'error',
    },
  },

  {
    files: ['services/menu/**/*.js'],
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'module',
      globals: globals.node,
    },
  },

  {
    files: ['src/GrotsPizzeria.Client/wwwroot/js/**/*.js'],
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'script',
      globals: globals.browser,
    },
  },

  {
    files: ['**/swagger-ui/*.js'],
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'script',
      globals: { ...globals.browser, SwaggerUIBundle: 'readonly' },
    },
  },
];
