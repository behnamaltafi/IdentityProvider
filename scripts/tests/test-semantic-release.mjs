import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { analyzeCommits } from '@semantic-release/commit-analyzer';
const require = createRequire(import.meta.url);
const config = require('../../release.config.cjs');
const options = config.plugins[0][1];
assert.deepEqual(config.branches, ['master']);
assert.equal(config.tagFormat, 'v${version}');
assert.equal(config.dryRun, true);
const plugins = config.plugins.map(p => Array.isArray(p) ? p[0] : p);
assert(plugins.includes('@semantic-release/changelog'));
assert(plugins.includes('@semantic-release/exec'));
assert(plugins.includes('@semantic-release/github'));
assert(!plugins.some(p => ['@semantic-release/npm','@semantic-release/git'].includes(p)));
const cases = [
  ['feat: add inventory', 'minor'], ['fix: correct total', 'patch'],
  ['perf: improve lookup', 'patch'], ['feat!: change contract', 'major'],
  ['fix(api)!: change response', 'major'],
  ['revert: restore earlier behavior', 'patch'],
  ['docs!: replace public contract', 'major'],
  ['chore: replace format\n\nBREAKING CHANGE: remove old format', 'major'],
  ...['docs', 'style', 'chore', 'test', 'ci', 'build', 'refactor', 'migration']
    .map(type => [`${type}: update details`, null])
];
for (const [message, expected] of cases) {
  const actual = await analyzeCommits(options, {
    cwd: process.cwd(), commits: [{ hash: 'test', message }], logger: { log() {} }
  });
  assert.equal(actual, expected, message);
  console.log(`PASS ${message.split('\n')[0]} -> ${actual ?? 'no release'}`);
}
