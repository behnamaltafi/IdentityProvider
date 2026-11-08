const fs = require('node:fs');
const path = require('node:path');
const project = require('./project.cjs');
exports.verifyConditions = async (_, context) => {
  if (!context.options.dryRun) {
    if ((process.env.CICD_ALLOW_PUBLISH ?? process.env.BONAK_ALLOW_PUBLISH) !== 'explicitly-approved') throw new Error('Explicit stable release approval required');
    const { masterGuard } = await import('./lib.mjs');
    masterGuard();
  }
};
exports.prepare = async (_, context) => {
  const { releasePath, published, verifyRelease, writeJson } = await import('./lib.mjs');
  fs.mkdirSync('dist', { recursive: true });
  const existing = path.join(releasePath(context.nextRelease.version), 'manifest.json');
  if (fs.existsSync(existing)) {
    const stored = verifyRelease(releasePath(context.nextRelease.version));
    if (stored.sourceCommit !== context.nextRelease.gitHead) throw new Error('Immutable version already belongs to another commit');
    // The exec prepare hook will restore the complete existing payload without rebuilding.
    fs.copyFileSync(path.join(releasePath(stored.version), 'CHANGELOG.md'), 'dist/CHANGELOG.md');
  }
  const previous = context.lastRelease.version;
  if (previous && !fs.existsSync(existing)) {
    const store = await import('../artifacts/nexus.mjs');
    if (store.nexusEnabled()) await store.ensureNexusRelease(previous);
    published(previous);
    fs.copyFileSync(path.join(releasePath(previous), 'CHANGELOG.md'), 'dist/CHANGELOG.md');
  } else if (!fs.existsSync(existing) && fs.existsSync('dist/CHANGELOG.md')) {
    throw new Error('First-release workspace must not contain an unrelated changelog');
  }
  // Stored release files are read-only. Only the disposable working copy may be
  // updated by the changelog plugin; never change the retained original.
  for (const file of ['dist/CHANGELOG.md', 'dist/release-notes.md']) {
    if (fs.existsSync(file)) fs.chmodSync(file, 0o644);
  }
  fs.writeFileSync('dist/release-notes.md', `# ${project.zipPrefix} ${context.nextRelease.version}\n\n${context.nextRelease.notes}\n`);
  writeJson('artifacts/release-context.json', {
    version: context.nextRelease.version, gitTag: context.nextRelease.gitTag,
    sourceCommit: context.nextRelease.gitHead, previousVersion: previous ?? null
  });
};
