// Review the candidate's real commits on an isolated master mirror. Never updates lab master.
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import semanticRelease from 'semantic-release';
import { git, run, writeJson } from './lib.mjs';
const require=createRequire(import.meta.url);
const root=path.resolve('artifacts/review-'+Date.now());fs.mkdirSync(root,{recursive:true});
const bare=path.join(root,'candidate.git'),checkout=path.join(root,'checkout');
run('git',['clone','--bare',git('remote','get-url','origin'),bare]);
git('push',pathToFileURL(bare).href,'HEAD:refs/heads/master'); // A normal fast-forward in the separate preview remote only.
run('git',['clone','--branch','master',pathToFileURL(bare).href,checkout]);
const config=require('../../release.config.cjs');
const result=await semanticRelease({...config,plugins:config.plugins.filter(p=>!Array.isArray(p)||p[0]!=='@semantic-release/github'),repositoryUrl:pathToFileURL(bare).href,dryRun:true,noCi:true},
  {cwd:checkout,env:{...process.env,JENKINS_URL:'',CI:''}});
writeJson('artifacts/candidate-release-plan.json',{scope:'isolated review mirror; real lab master unchanged',sourceCommit:git('rev-parse','HEAD'),release:result?result.nextRelease:null});
if(result)fs.writeFileSync('artifacts/candidate-release-notes.md',result.nextRelease.notes);
console.log(`Review preview complete: ${result?.nextRelease.version ?? 'no release'}`);
