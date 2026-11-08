import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { createRequire } from 'node:module';
import { branchName, inventory, manifestValid, registryManifest, sha, verifyRelease, writeJson } from '../release/lib.mjs';
import { configHash, prerequisite, readConfiguration, rollbackEligible, validateApproval } from '../deploy/deploy.mjs';
const require = createRequire(import.meta.url);
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'bonak-pipeline-tests-'));
const manifest = () => ({ application: 'IdentityProvider.Api', version: '1.2.3', gitTag: 'v1.2.3', sourceCommit: 'a'.repeat(40), sourceTree: 'b'.repeat(40), buildNumber: '17', zipFile: 'IdentityProvider-1.2.3.zip', zipSha256: sha('zip-fixture'), imageRepository: 'localhost:5500/identityprovider/api', imageDigest: `sha256:${'c'.repeat(64)}`, createdAtUtc: new Date().toISOString() });
function fixture() {
  const directory = fs.mkdtempSync(path.join(root, 'release-'));
  fs.mkdirSync(path.join(directory, 'packages'));
  fs.writeFileSync(path.join(directory, 'packages/IdentityProvider-1.2.3.zip'), 'zip-fixture');
  writeJson(path.join(directory, 'manifest.json'), manifest());
  fs.writeFileSync(path.join(directory, 'release-notes.md'), 'Actual notes fixture');
  fs.writeFileSync(path.join(directory, 'SHA256SUMS'), inventory(directory));
  return directory;
}
test('branch metadata supports detached Jenkins checkouts and rejects malformed names', () => {
  for (const branch of ['master','develop','feature/test','bugfix/total','release/next','hotfix/api']) assert.equal(branchName({ BRANCH_NAME: branch }), branch);
  assert.equal(branchName({ GIT_BRANCH: 'origin/master' }), 'master');
  for (const branch of ['main','refs/tags/v1.0.0','feature/../master','feature/x;touch bad','feature/']) assert.throws(() => branchName({ BRANCH_NAME: branch }));
});
test('manifest rejects traversal, inconsistent version, malformed SHA and mutable image references', () => {
  assert.equal(manifestValid(manifest()).version, '1.2.3');
  for (const change of [{ zipFile: '../file.zip' }, {gitTag:'v2.0.0'}, {sourceCommit:'invented'}, {imageDigest:':latest'}, {version:'01.2.3'}]) assert.throws(() => manifestValid({...manifest(),...change}));
});
test('inventory detects corrupted ZIP, altered notes, extra files and missing files', () => {
  for (const mutate of [d=>fs.appendFileSync(path.join(d,'packages/IdentityProvider-1.2.3.zip'),'bad'), d=>fs.writeFileSync(path.join(d,'release-notes.md'),'changed'), d=>fs.writeFileSync(path.join(d,'extra'),'bad'), d=>fs.unlinkSync(path.join(d,'release-notes.md'))]) {
    const directory = fixture(); assert.equal(verifyRelease(directory).version,'1.2.3'); mutate(directory); assert.throws(()=>verifyRelease(directory));
  }
});
test('registry verifies digest against actual returned body and requested digest', async () => {
  const original = globalThis.fetch;
  const bytes = Buffer.from('{"schemaVersion":2}');
  const digest = `sha256:${sha(bytes)}`;
  try {
    globalThis.fetch = async()=>new Response(bytes,{status:200,headers:{'docker-content-digest':digest}});
    assert.equal(await registryManifest(manifest().imageRepository,digest),digest);
    await assert.rejects(()=>registryManifest(manifest().imageRepository,manifest().imageDigest));
    globalThis.fetch = async()=>new Response('different',{status:200,headers:{'docker-content-digest':digest}});
    await assert.rejects(()=>registryManifest(manifest().imageRepository,'1.2.3'));
    globalThis.fetch = async()=>new Response('',{status:404});
    assert.equal(await registryManifest(manifest().imageRepository,'1.2.3',false),null);
  } finally { globalThis.fetch = original; }
});
test('UAT/Production require a healthy preceding environment for the exact digest', () => {
  const old = process.env.ARTIFACT_ROOT; process.env.ARTIFACT_ROOT=path.join(root,'store');
  try {
    assert.throws(()=>prerequisite('uat',manifest()));
    const record={id:'test',status:'healthy',version:'1.2.3',imageDigest:manifest().imageDigest,finishedAtUtc:new Date().toISOString()};
    writeJson(path.join(process.env.ARTIFACT_ROOT,'deployments/internal/test.json'),record);
    assert.equal(prerequisite('uat',manifest()).id,'test');
    assert.throws(()=>prerequisite('production',manifest()));
    assert.throws(()=>prerequisite('uat',{...manifest(),imageDigest:`sha256:${'d'.repeat(64)}`}));
  } finally { if(old===undefined)delete process.env.ARTIFACT_ROOT;else process.env.ARTIFACT_ROOT=old; }
});
test('runtime configuration fingerprint changes when compose or secret-bearing env changes', () => {
  const directory=fs.mkdtempSync(path.join(root,'config-'));
  fs.writeFileSync(path.join(directory,'compose.yml'),'services: {}');fs.writeFileSync(path.join(directory,'.env.runtime'),'VALUE=1');
  const before=configHash(directory),snapshot=readConfiguration(directory);fs.writeFileSync(path.join(directory,'.env.runtime'),'VALUE=2');assert.notEqual(configHash(directory),before);
  assert.equal(snapshot.hash,before);assert.equal(snapshot.files['.env.runtime'].toString(),'VALUE=1');
});
test('publication defaults to preview and uses only the permitted semantic-release lifecycle', () => {
  const c=require('../../release.config.cjs'); assert.deepEqual(c.branches,['master']);assert.equal(c.dryRun,true);
  assert(c.plugins.some(p=>Array.isArray(p)&&p[0]==='@semantic-release/exec'&&p[1].prepareCmd.includes('${nextRelease.version}')));
});

test('portable release approval reaches the master/PR guard and never bypasses it', async () => {
  const context = require('../release/context.cjs');
  const keys = ['CICD_ALLOW_PUBLISH', 'BONAK_ALLOW_PUBLISH', 'CHANGE_ID', 'BRANCH_NAME'];
  const original = Object.fromEntries(keys.map(key => [key, process.env[key]]));
  try {
    delete process.env.BONAK_ALLOW_PUBLISH;
    delete process.env.CICD_ALLOW_PUBLISH;
    await assert.rejects(context.verifyConditions({}, {options:{dryRun:false}}), /Explicit stable release approval required/);
    process.env.CICD_ALLOW_PUBLISH = 'explicitly-approved';
    process.env.BRANCH_NAME = 'master';
    process.env.CHANGE_ID = 'fixture-pr';
    await assert.rejects(context.verifyConditions({}, {options:{dryRun:false}}), /restricted to master, never PR/);
  } finally {
    for (const key of keys) { if (original[key] === undefined) delete process.env[key]; else process.env[key] = original[key]; }
  }
});
test('Production approval is bound to actor, build, operation, digest, config and time', () => {
  const now=Date.now(),m=manifest();
  const approval={source:'jenkins-input',approver:'fixture-operator',environment:'production',buildUrl:'fixture-build',version:m.version,imageDigest:m.imageDigest,configHash:'hash',operation:'deploy',approvedAtUtc:new Date(now-1000).toISOString()};
  const context={manifest:m,buildUrl:'fixture-build',hash:'hash',operation:'deploy',now};
  assert.equal(validateApproval(approval,context).approver,'fixture-operator');
  for(const change of [{approver:''},{source:'boolean-env'},{buildUrl:'another-build'},{imageDigest:`sha256:${'d'.repeat(64)}`},{configHash:'changed'},{operation:'rollback'},{approvedAtUtc:new Date(now-90000000).toISOString()},{approvedAtUtc:new Date(now+1000).toISOString()}]) assert.throws(()=>validateApproval({...approval,...change},context));
});
test('rollback accepts only previously healthy exact digests with compatible configuration', () => {
  const m=manifest();const good={status:'healthy',version:m.version,imageDigest:m.imageDigest,configHash:'same'};
  assert.equal(rollbackEligible([good],m,'same'),good);
  assert.throws(()=>rollbackEligible([],m,'same'));
  assert.throws(()=>rollbackEligible([{...good,status:'failed'}],m,'same'));
  assert.throws(()=>rollbackEligible([good],m,'changed'));
});
test('next release restores read-only archived CHANGELOG and updates only its working copy', async () => {
  const originalDirectory=process.cwd(),originalRoot=process.env.ARTIFACT_ROOT;
  const store=path.join(root,'cumulative-store'),stored=path.join(store,'releases','1.2.3');
  fs.mkdirSync(path.dirname(stored),{recursive:true});fs.cpSync(fixture(),stored,{recursive:true});
  fs.writeFileSync(path.join(stored,'CHANGELOG.md'),'# 1.2.3\n\nPrevious retained release\n');
  fs.writeFileSync(path.join(stored,'SHA256SUMS'),inventory(stored));
  fs.chmodSync(path.join(stored,'CHANGELOG.md'),0o444);
  writeJson(path.join(store,'published','1.2.3.json'),{sourceCommit:manifest().sourceCommit,imageDigest:manifest().imageDigest});
  const working=fs.mkdtempSync(path.join(root,'changelog-work-'));
  process.env.ARTIFACT_ROOT=store;process.chdir(working);
  try {
    const context={cwd:working,lastRelease:{version:'1.2.3'},nextRelease:{version:'1.2.4',gitTag:'v1.2.4',gitHead:'d'.repeat(40),notes:'# 1.2.4\n\nFix pooled resources'},logger:{log(){}}};
    await require('../release/context.cjs').prepare({},context);
    const changelog=await import('@semantic-release/changelog');
    await changelog.prepare({changelogFile:'dist/CHANGELOG.md'},context);
    const combined=fs.readFileSync('dist/CHANGELOG.md','utf8');
    assert(combined.includes('1.2.4')&&combined.includes('1.2.3'));
    assert.equal(fs.readFileSync(path.join(stored,'CHANGELOG.md'),'utf8'),'# 1.2.3\n\nPrevious retained release\n');
    assert.equal(verifyRelease(stored).version,'1.2.3');
  } finally {
    process.chdir(originalDirectory);
    if(originalRoot===undefined)delete process.env.ARTIFACT_ROOT;else process.env.ARTIFACT_ROOT=originalRoot;
  }
});
