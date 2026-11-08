module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [2, 'always', [
      'feat', 'fix', 'perf', 'refactor', 'chore', 'docs', 'style',
      'test', 'ci', 'build', 'revert', 'migration'
    ]]
  }
};
