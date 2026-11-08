import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import project from './project.cjs';
export const versionPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
export function must(ok, message) { if (!ok) throw new Error(message); }
export function run(file, args, options = {}) {
  const result = spawnSync(file, args, { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024, stdio: options.echo ? 'inherit' : 'pipe', ...options });
  if (result.error || result.status !== 0) throw new Error(`${file} ${args.join(' ')} failed (${result.status}): ${result.error?.message ?? result.stderr ?? ''}`);
  if (options.echo) process.stderr.write((result.stdout ?? '') + (result.stderr ?? ''));
  return (result.stdout ?? '').trim();
}
export function git(...args) { return run('git', args); }
export const sha = data => crypto.createHash('sha256').update(data).digest('hex');
export const readJson = file => JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
export function writeJson(file, value, exclusive = false) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: exclusive ? 'wx' : 'w' });
}
export function filesIn(root, relative = '') {
  return fs.readdirSync(path.join(root, relative), { withFileTypes: true }).sort((a,b) => a.name.localeCompare(b.name)).flatMap(entry => {
    must(!entry.isSymbolicLink(), 'Release payload cannot contain symlinks');
    const name = path.posix.join(relative, entry.name);
    return entry.isDirectory() ? filesIn(root, name) : [name];
  });
}
export function manifestValid(m) {
  must(m.application === project.application && versionPattern.test(m.version), 'Invalid application/version');
  must(m.gitTag === `v${m.version}`, 'Tag/version mismatch');
  for (const key of ['sourceCommit', 'sourceTree']) must(/^[a-f0-9]{40}$/.test(m[key]), `Invalid ${key}`);
  must(typeof m.buildNumber === 'string' && m.buildNumber.length > 0, 'Missing build number');
  must(m.zipFile === `${project.zipPrefix}-${m.version}.zip`, 'Unsafe ZIP path');
  must(/^[a-f0-9]{64}$/.test(m.zipSha256), 'Invalid ZIP hash');
  must(/^[a-z0-9.-]+:\d+\/[a-z0-9/._-]+$/.test(m.imageRepository), 'Invalid image repository');
  must(/^sha256:[a-f0-9]{64}$/.test(m.imageDigest), 'Invalid image digest');
  must(!Number.isNaN(Date.parse(m.createdAtUtc)), 'Invalid creation timestamp');
  return m;
}
export function inventory(root) {
  return filesIn(root).filter(f => f !== 'SHA256SUMS').map(f => `${sha(fs.readFileSync(path.join(root, f)))}  ${f}`).join('\n') + '\n';
}
export function verifyRelease(root) {
  const m = manifestValid(readJson(path.join(root, 'manifest.json')));
  const recorded = fs.readFileSync(path.join(root, 'SHA256SUMS'), 'utf8');
  must(recorded === inventory(root), 'Release checksum inventory mismatch (missing, extra or modified file)');
  must(sha(fs.readFileSync(path.join(root, 'packages', m.zipFile))) === m.zipSha256, 'ZIP checksum mismatch');
  return m;
}
export function artifactRoot() { return path.resolve(process.env.ARTIFACT_ROOT ?? project.artifactRoot); }
export function releasePath(version) { must(versionPattern.test(version), 'Invalid version'); return path.join(artifactRoot(), 'releases', version); }
export function branchName(env = process.env) {
  const value = env.BRANCH_NAME || env.GIT_LOCAL_BRANCH || env.GIT_BRANCH || git('branch', '--show-current');
  const branch = value.replace(/^origin\//, '').replace(/^refs\/heads\//, '');
  must(/^(master|develop|(feature|bugfix|release|hotfix)\/[a-zA-Z0-9][a-zA-Z0-9._/-]*)$/.test(branch) && !branch.includes('..') && !branch.endsWith('/'), 'Unsupported Git Flow branch');
  return branch;
}
export function masterGuard() {
  must(branchName() === 'master' && !process.env.CHANGE_ID, 'Stable publication is restricted to master, never PR builds');
  must(git('rev-parse', '--is-shallow-repository') === 'false', 'Full Git history required');
  git('fetch', 'origin', '--tags');
  must(git('rev-parse', 'HEAD') === git('rev-parse', 'origin/master'), 'HEAD must exactly match origin/master');
  must(git('status', '--porcelain', '--untracked-files=no') === '', 'Tracked source must be clean');
}
export async function registryManifest(repository, reference, required = true) {
  const name = repository.slice(repository.indexOf('/') + 1);
  const base = process.env.REGISTRY_API_URL ?? 'http://registry:5000';
  const response = await fetch(`${base}/v2/${name}/manifests/${encodeURIComponent(reference)}`, {
    headers: { Accept: 'application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.index.v1+json' }, signal: AbortSignal.timeout(30000)
  });
  if (response.status === 404 && !required) return null;
  must(response.ok, `Registry manifest request failed: ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  const digest = response.headers.get('docker-content-digest');
  must(digest === `sha256:${sha(bytes)}`, 'Registry digest/body mismatch');
  if (reference.startsWith('sha256:')) must(digest === reference, 'Registry returned a different digest');
  return digest;
}
export function published(version) {
  const m = verifyRelease(releasePath(version));
  const marker = readJson(path.join(artifactRoot(), 'published', `${version}.json`));
  must(marker.sourceCommit === m.sourceCommit && marker.imageDigest === m.imageDigest, 'Release is not finalized/published');
  return m;
}
