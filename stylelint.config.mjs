/** @type {import('stylelint').Config} */
export default {
  extends: ['stylelint-config-standard'],
  ignoreFiles: ['**/node_modules/**', '**/bin/**', '**/obj/**', '**/target/**', '**/.venv/**', '**/_framework/**'],
  overrides: [
    {
      files: ['**/swagger-ui/theme.css'],
      rules: {
        'selector-class-pattern': null,
        'no-descending-specificity': null,
      },
    },
  ],
};
