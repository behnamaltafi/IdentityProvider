// CLI remains a preview by default. Only the guarded runner may publish.
const parserOpts = {
  headerPattern: /^(\w*)(?:\((.*)\))?!?: (.*)$/,
  breakingHeaderPattern: /^(\w*)(?:\((.*)\))?!: (.*)$/,
  headerCorrespondence: ['type', 'scope', 'subject'],
  noteKeywords: ['BREAKING CHANGE', 'BREAKING-CHANGE']
};
module.exports = {
  branches: ['master'],
  tagFormat: 'v${version}',
  dryRun: true,
  plugins: [
    ['@semantic-release/commit-analyzer', {
      preset: 'angular',
      parserOpts,
      releaseRules: [
        { breaking: true, release: 'major' },
        { type: 'feat', release: 'minor' },
        { type: 'fix', release: 'patch' },
        { type: 'perf', release: 'patch' },
        { type: 'revert', release: 'patch' }
      ]
    }],
    ['@semantic-release/release-notes-generator', {
      preset: 'angular',
      linkCompare: false,
      linkReferences: false,
      parserOpts
    }],
    './scripts/release/context.cjs',
    ['@semantic-release/changelog', { changelogFile: 'dist/CHANGELOG.md' }],
    ['@semantic-release/exec', {
      prepareCmd: 'node scripts/release/lifecycle.mjs prepare ${nextRelease.version}',
      publishCmd: 'node scripts/release/lifecycle.mjs finalize ${nextRelease.version}'
    }],
    ['@semantic-release/github', {
      assets: [
        { path: 'dist/packages/*.zip', label: 'Immutable application ZIP' },
        { path: 'dist/manifest.json', label: 'Release manifest' },
        { path: 'dist/SHA256SUMS', label: 'SHA256 inventory' },
        { path: 'dist/CHANGELOG.md', label: 'Cumulative changelog' },
        { path: 'dist/release-notes.md', label: 'Release notes' }
      ],
      successComment: false, failComment: false, failTitle: false,
      labels: false, releasedLabels: false, addReleases: false
    }]
  ]
};
