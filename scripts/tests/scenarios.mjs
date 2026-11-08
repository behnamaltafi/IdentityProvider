// Real isolated Git/semantic-release integration tests. Never use the lab origin.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import semanticRelease from 'semantic-release';
import { run, writeJson } from '../release/lib.mjs';
const require=createRequire(import.meta.url);
const project=process.cwd();
const config=require('../../release.config.cjs');
const root=fs.mkdtempSync(path.join(os.tmpdir(),'bonak-real-scenarios-'));
const working=path.join(root,'checkout'),remote=path.join(root,'fixture.git');
fs.mkdirSync(working);
const git=(...args)=>run('git',args,{cwd:working});
const name=run('git',['config','user.name']),email=run('git',['config','user.email']);
assert(name&&email,'Use the real configured Git identity, never invent one');
run('git',['init','--bare','-b','master',remote]);git('init','-b','master');git('config','user.name',name);git('config','user.email',email);
git('remote','add','origin',pathToFileURL(remote).href);
const commit=(message)=>{fs.appendFileSync(path.join(working,'state.txt'),message+'\n');git('add','state.txt');git('commit','-m',message);};
const plugins=config.plugins.filter(p=>Array.isArray(p)&&['@semantic-release/commit-analyzer','@semantic-release/release-notes-generator','@semantic-release/changelog'].includes(p[0])).map(p=>p[0]==='@semantic-release/changelog'?[p[0],{changelogFile:'CHANGELOG.md'}]:p);
const env={...process.env,JENKINS_URL:'',CI:'',GIT_AUTHOR_NAME:name,GIT_AUTHOR_EMAIL:email,GIT_COMMITTER_NAME:name,GIT_COMMITTER_EMAIL:email};
async function release(dryRun=true){return semanticRelease({branches:['master'],tagFormat:'v${version}',repositoryUrl:pathToFileURL(remote).href,plugins,dryRun,noCi:true},{cwd:working,env});}
commit('feat: support inventory acceptance fixture');git('push','-u','origin','master');
const plan=await release();assert.equal(plan.nextRelease.version,'1.0.0');assert.equal(git('tag','--list'),'');
const first=await release(false);assert.equal(first.nextRelease.version,'1.0.0');
assert.equal(git('tag','--list'),'v1.0.0');assert(fs.readFileSync(path.join(working,'CHANGELOG.md'),'utf8').includes('inventory'));
// Model archival/restoration instead of pretending this untracked file is in Git.
const archive=path.join(root,'archived-CHANGELOG.md');fs.copyFileSync(path.join(working,'CHANGELOG.md'),archive);fs.unlinkSync(path.join(working,'CHANGELOG.md'));fs.copyFileSync(archive,path.join(working,'CHANGELOG.md'));
commit('fix: correct inventory fixture total');git('push','origin','master');
assert.equal((await release()).nextRelease.version,'1.0.1');await release(false);
const history=fs.readFileSync(path.join(working,'CHANGELOG.md'),'utf8');assert(history.includes('1.0.0')&&history.includes('1.0.1')&&history.includes('correct inventory'));
const tags=git('tag','--list');commit('docs: explain fixture usage');git('push','origin','master');assert.equal(await release(),false);assert.equal(git('tag','--list'),tags);
git('switch','-c','develop');git('push','-u','origin','develop');
for(const branch of ['feature/acceptance','release/acceptance']){git('switch','-c',branch,'develop');commit('feat: extend acceptance-only fixture');git('push','-u','origin',branch);assert.equal(await release(),false);assert.equal(git('tag','--list'),tags);}
// No hook installed: a malformed commit must still be rejected by the real CI validator.
commit('invalid commit deliberately bypasses local hook');
let rejected=false;try{run('pwsh',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(project,'scripts/ci/Test-Commits.ps1'),'-RepositoryPath',working]);}catch{rejected=true;}assert(rejected);
writeJson(path.join(project,'artifacts/scenario-results.json'),{fixtureRoot:root,firstVersion:'1.0.0',patchVersion:'1.0.1',featureNoRelease:true,releaseBranchNoRelease:true,docsNoRelease:true,hookBypassRejected:true,scope:'isolated fixture only; official lab origin untouched'});
console.log(`PASS real Git/semantic-release scenarios; isolated evidence: ${root}`);
