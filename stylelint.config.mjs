/** @type {import('stylelint').Config} */
export default {
  extends: ['stylelint-config-standard'],
  ignoreFiles: ['**/node_modules/**', '**/bin/**', '**/obj/**', '**/target/**', '**/.venv/**', '**/_framework/**'],
};
