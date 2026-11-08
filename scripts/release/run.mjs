import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import semanticRelease from 'semantic-release';
import { createRequire } from 'node:module';
import { artifactRoot, branchName, git, masterGuard, must, writeJson } from './lib.mjs';
const require = createRequire(import.meta.url);
const config = require('../../release.config.cjs');
const mode = process.argv[2] ?? 'preview';
must(['preview', 'publish'].includes(mode), 'Use preview or publish');
const branch = branchName();
if (mode === 'publish') {
  must((process.env.CICD_ALLOW_PUBLISH ?? process.env.BONAK_ALLOW_PUBLISH) === 'explicitly-approved', 'Publication has not been explicitly approved');
  must(process.env.JENKINS_URL && process.env.BUILD_URL && /^\d+$/.test(process.env.BUILD_NUMBER ?? ''), 'Official publication requires actual Jenkins build metadata');
  masterGuard();
}
const lock = path.join(artifactRoot(), '.locks', 'release');
let locked = false;
try {
  if (mode === 'publish') {
    fs.mkdirSync(path.dirname(lock), { recursive: true });
    fs.mkdirSync(lock); // Exclusive across all jobs and workspaces, no unsafe stale-lock takeover.
    locked = true;
    writeJson(path.join(lock, 'owner.json'), { build: process.env.BUILD_URL, pid: process.pid, at: new Date().toISOString() });
  }
  const env = { ...process.env };
  // env-ci prefers GIT_LOCAL_BRANCH/GIT_BRANCH to BRANCH_NAME. Normalize SCM metadata
  // on detached Jenkins checkouts instead of inventing a local branch or changing HEAD.
  if (env.JENKINS_URL) Object.assign(env, { GIT_LOCAL_BRANCH: branch, GIT_BRANCH: branch, BRANCH_NAME: branch });
  const remote = git('remote', 'get-url', 'origin');
  if (mode === 'publish') must(remote === require('./project.cjs').repositoryUrl, 'Migrate and verify GitHub origin before official publication');
  const repositoryUrl = /^[A-Za-z]:[\\/]/.test(remote) ? pathToFileURL(remote).href : remote;
  // Offline/local previews omit GitHub authentication and publication hooks only.
  // Official publication always includes the GitHub plugin and its verify step.
  const plugins = mode === 'publish' ? config.plugins : config.plugins.filter(p => !Array.isArray(p) || p[0] !== '@semantic-release/github');
  const result = await semanticRelease({ ...config, plugins, dryRun: mode !== 'publish', repositoryUrl }, { env });
  writeJson('artifacts/release-plan.json', { branch, sourceCommit: git('rev-parse', 'HEAD'), release: result ? result.nextRelease : null, dryRun: mode !== 'publish' });
  if (mode === 'publish' && result) must(fs.existsSync(path.join(artifactRoot(), 'published', `${result.nextRelease.version}.json`)), 'Publication did not finalize a manifest');
} finally {
  if (locked) fs.rmSync(lock, { recursive: true }); // Only this invocation's acquired lock.
}
