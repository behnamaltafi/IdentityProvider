import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import os from 'node:os';
import project from '../release/project.cjs';
import {nexusEnabled, ensureNexusRelease, syncNexusReleases} from '../artifacts/nexus.mjs';
import { artifactRoot, manifestValid, must, published, readJson, registryManifest, run, sha, versionPattern, writeJson } from '../release/lib.mjs';
export const environments = project.environments;
export function configHash(directory) {
  return sha(Buffer.concat(['compose.yml', '.env.runtime'].map(name => fs.readFileSync(path.join(directory, name)))));
}
export function readConfiguration(directory) {
  const files = Object.fromEntries(['compose.yml', '.env.runtime'].map(name => [name, fs.readFileSync(path.join(directory, name))]));
  return { files, hash: sha(Buffer.concat(Object.values(files))) };
}
export function recordPath(environment, id) {
  must(environments.includes(environment) && /^[a-f0-9-]{36}$/.test(id), 'Invalid environment/record identifier');
  return path.join(artifactRoot(), 'deployments', environment, `${id}.json`);
}
function history(environment) {
  const folder = path.join(artifactRoot(), 'deployments', environment);
  return fs.existsSync(folder) ? fs.readdirSync(folder).filter(f => f.endsWith('.json')).map(f => readJson(path.join(folder, f))).sort((a,b) => b.finishedAtUtc.localeCompare(a.finishedAtUtc)) : [];
}
export function latest(environment, digest) { return history(environment).find(r => r.status === 'healthy' && (!digest || r.imageDigest === digest)); }
export function prerequisite(environment, m) {
  const index = environments.indexOf(environment);
  must(index >= 0, 'Unknown environment');
  const required = index > 0 ? environments[index - 1] : null;
  if (required) {
    const record = latest(required, m.imageDigest);
    must(record && record.version === m.version, `Successful ${required} deployment of the exact digest is required`);
    return record;
  }
  return null;
}
export function validateApproval(approval, { manifest: m, buildUrl, hash, operation, now = Date.now() }) {
  must(approval.source === 'jenkins-input' && approval.approver && approval.environment === 'production' && approval.buildUrl === buildUrl && approval.version === m.version && approval.imageDigest === m.imageDigest && approval.configHash === hash && approval.operation === operation, 'Approval is not bound to this operation/build/digest/config');
  const age = now - Date.parse(approval.approvedAtUtc);
  must(age >= 0 && age < 24 * 60 * 60 * 1000, 'Approval expired or has a future timestamp');
  return approval;
}
export function rollbackEligible(records, m, hash) {
  const prior = records.find(r => r.status === 'healthy' && r.version === m.version && r.imageDigest === m.imageDigest);
  must(prior, 'Rollback target was never healthy in this environment');
  must(prior.configHash === hash, 'Runtime configuration changed since target deployment; explicit compatibility review required');
  return prior;
}
export async function main(args) {
  const [action, environment, version, approvalId] = args;
  const root = artifactRoot();
  if (action === 'list' || action === 'versions') {
    if (nexusEnabled()) await syncNexusReleases();
    const releases = path.join(root, 'published');
    const valid = fs.existsSync(releases) ? fs.readdirSync(releases).filter(f => f.endsWith('.json')).flatMap(file => {
      try { const m = published(file.slice(0, -5)); return [{ version: m.version, digest: m.imageDigest, source: m.sourceCommit }]; } catch { return []; }
    }) : [];
    console.log(action === 'versions' ? valid.map(m => m.version).join('\n') : JSON.stringify(valid, null, 2)); return;
  }
  must(['deploy', 'rollback', 'approve'].includes(action) && environments.includes(environment) && versionPattern.test(version), 'Expected list or deploy/rollback/approve ENV VERSION [approval-id]');
  if (nexusEnabled()) await ensureNexusRelease(version);
  const m = manifestValid(published(version));
  await registryManifest(m.imageRepository, m.imageDigest);
  const directory = path.join(process.env.ENVIRONMENT_ROOT ?? project.environmentRoot, environment);
  const configuration = readConfiguration(directory);
  const hash = configuration.hash;
  if (action === 'approve') {
    must(environment === 'production', 'Only production uses a stored approval');
    const approver = process.env.CICD_APPROVER ?? process.env.BONAK_APPROVER;
    must(approver && process.env.BUILD_URL && process.env.JENKINS_URL, 'Actual Jenkins input approver and build metadata required');
    const kind = process.env.CICD_OPERATION ?? process.env.BONAK_OPERATION ?? 'deploy';
    const uat = kind === 'rollback' ? null : prerequisite(environment, m);
    const id = crypto.randomUUID();
    writeJson(path.join(root, 'approvals', `${id}.json`), {
      id, source: 'jenkins-input', approver, buildUrl: process.env.BUILD_URL,
      environment, version, imageDigest: m.imageDigest, operation: kind, configHash: hash,
      uatRecordId: uat?.id ?? null, approvedAtUtc: new Date().toISOString()
    }, true);
    console.log(id); return;
  }
  const lock = path.join(root, '.locks', `deploy-${environment}`);
  fs.mkdirSync(path.dirname(lock), { recursive: true }); fs.mkdirSync(lock);
  const id = crypto.randomUUID();
  let snapshot;
  const record = { id, operation: action, environment, version, imageDigest: m.imageDigest, sourceCommit: m.sourceCommit,
    buildUrl: process.env.BUILD_URL ?? null, buildNumber: process.env.BUILD_NUMBER ?? null,
    configHash: hash, startedAtUtc: new Date().toISOString(), previousRecordId: latest(environment)?.id ?? null };
  try {
    if (action === 'rollback') {
      rollbackEligible(history(environment), m, hash);
      must(latest(environment)?.imageDigest !== m.imageDigest, 'Selected digest is already the current healthy deployment');
    } else prerequisite(environment, m);
    if (environment === 'production') {
      must(/^[a-f0-9-]{36}$/.test(approvalId ?? ''), 'Production requires an actual Jenkins input approval record');
      const approval = readJson(path.join(root, 'approvals', `${approvalId}.json`));
      validateApproval(approval, {manifest:m, buildUrl:process.env.BUILD_URL, hash, operation:action});
      if (action !== 'rollback') must(prerequisite(environment, m).id === approval.uatRecordId, 'UAT state changed after approval');
      // An approval is single-use, including failed attempts. Another attempt needs new input.
      writeJson(path.join(root, 'used-approvals', `${approvalId}.json`), { deploymentId: id }, true);
      record.approvedBy = approval.approver; record.approvalId = approvalId;
    }
    const image = `${m.imageRepository}@${m.imageDigest}`;
    const env = { ...process.env, APP_IMAGE: image };
    // Compose consumes this private snapshot itself. It is never a daemon bind
    // source, and later edits of the host config cannot change approved bytes.
    snapshot = fs.mkdtempSync(path.join(os.tmpdir(), 'cicd-config-'));
    for (const [name, bytes] of Object.entries(configuration.files)) fs.writeFileSync(path.join(snapshot, name), bytes, {mode:0o600,flag:'wx'});
    // Optional context selects an SSH/TLS Docker endpoint; secrets stay in agent Docker config.
    const dockerContext = process.env.DEPLOY_DOCKER_CONTEXT ? ['--context', process.env.DEPLOY_DOCKER_CONTEXT] : [];
    const compose = [...dockerContext, 'compose', '-p', `${project.composeProjectPrefix}-${environment}`, '-f', path.join(snapshot, 'compose.yml')];
    run('docker', [...compose, 'config', '--quiet'], { env });
    run('docker', [...dockerContext, 'pull', image], { echo: true });
    run('docker', [...compose, 'up', '-d', '--no-build', '--pull', 'never', '--wait', '--wait-timeout', '120', 'api'], { env, echo: true });
    // A disposable wget probe runs on the environment network, never binds an agent workspace.
    run('docker', [...compose, '--profile', 'verify', 'run', '--rm', '--no-deps', 'smoke'], { env, echo: true });
    const container = run('docker', [...compose, 'ps', '-q', 'api'], { env });
    const state = JSON.parse(run('docker', [...dockerContext, 'inspect', container]))[0];
    must(state.Config.Image === image && state.State.Health.Status === 'healthy', 'Running application digest/health mismatch');
    record.status = 'healthy'; record.containerId = container;
  } catch (error) { record.status = 'failed'; record.error = error.message; throw error; }
  finally {
    record.finishedAtUtc = new Date().toISOString();
    try {
      writeJson(recordPath(environment, id), record, true);
      writeJson(path.join('artifacts', 'deployments', `${environment}-${id}.json`), record, true);
    }
    finally {
      if (snapshot) {
        const resolved = path.resolve(snapshot);
        must(resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('cicd-config-'), 'Unsafe generated snapshot cleanup path');
        fs.rmSync(resolved, {recursive:true,force:true});
      }
      fs.rmSync(lock, { recursive: true });
    }
  }
  console.log(JSON.stringify(record, null, 2));
}
if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'))) {
  await main(process.argv.slice(2));
}
