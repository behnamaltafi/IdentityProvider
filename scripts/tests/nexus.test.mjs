import test,{before,after,beforeEach} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import {spawnSync} from 'node:child_process';
import {inventory,sha,verifyRelease,writeJson} from '../release/lib.mjs';
import {nexusClient,nexusSettings,prepareNexusRelease,finalizeNexusRelease,ensureNexusRelease,syncNexusReleases,extractPublish} from '../artifacts/nexus.mjs';

const root=fs.mkdtempSync(path.join(os.tmpdir(),'nexus-contract-tests-'));
const previousRoot=process.env.ARTIFACT_ROOT;
const objects=new Map();const writes=[];let failAt='',redirectAt='',paginationLoop=false,leadingSlash=false,base;
const auth=user=>'Basic '+Buffer.from(user+':fixture-password').toString('base64');
const server=http.createServer(async(req,res)=>{
 const user=req.headers.authorization===auth('publisher')?'publisher':req.headers.authorization===auth('reader')?'reader':null;
 if(!user){res.writeHead(401);return res.end();}
 const url=new URL(req.url,'http://localhost');
 if(url.pathname==='/service/rest/v1/assets'){
  const all=[...objects.keys()].map(p=>({path:(leadingSlash?'/':'')+p}));const next=url.searchParams.get('continuationToken');
  res.setHeader('Content-Type','application/json');
  return res.end(JSON.stringify({items:next?all.slice(2):all.slice(0,2),continuationToken:paginationLoop?'repeated':(!next&&all.length>2?'page2':null)}));
 }
 const prefix='/repository/test-releases/';
 if(!url.pathname.startsWith(prefix)){res.writeHead(404);return res.end();}
 const name=decodeURIComponent(url.pathname.slice(prefix.length));
 if(req.method==='GET'){
  if(name===redirectAt){res.writeHead(302,{Location:'http://localhost:1/forbidden'});return res.end();}
  if(!objects.has(name)){res.writeHead(404);return res.end();}
  return res.end(objects.get(name));
 }
 if(req.method==='PUT'){
  if(user!=='publisher'){res.writeHead(403);return res.end();}
  if(name===failAt){res.writeHead(503);return res.end();}
  if(objects.has(name)){res.writeHead(400);return res.end();}
  const data=[];for await(const chunk of req)data.push(chunk);
  objects.set(name,Buffer.concat(data));writes.push(name);res.writeHead(201);return res.end();
 }
 res.writeHead(405);res.end();
});
const tools=spawnSync('zip',['-v']).status===0&&spawnSync('unzip',['-v']).status===0;
before(async()=>{if(process.env.CI)assert(tools,'CI must have zip/unzip for real release reconstruction tests');await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));base='http://127.0.0.1:'+server.address().port;});
after(async()=>{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));if(previousRoot===undefined)delete process.env.ARTIFACT_ROOT;else process.env.ARTIFACT_ROOT=previousRoot;});
beforeEach(()=>{objects.clear();writes.length=0;failAt='';redirectAt='';paginationLoop=false;leadingSlash=false;process.env.ARTIFACT_ROOT=fs.mkdtempSync(path.join(root,'store-'));});
const client=(user='publisher')=>nexusClient({NEXUS_BASE_URL:base,NEXUS_REPOSITORY:'test-releases',NEXUS_USERNAME:user,NEXUS_PASSWORD:'fixture-password'});
function release(version='1.2.3'){
 const folder=path.join(process.env.ARTIFACT_ROOT,'releases',version);
 fs.mkdirSync(path.join(folder,'publish'),{recursive:true});fs.mkdirSync(path.join(folder,'packages'));
 fs.writeFileSync(path.join(folder,'publish','IdentityProvider.Api.dll'),'fixed-release-fixture-bytes');
 fs.mkdirSync(path.join(folder,'publish/runtimes/linux-x64/native'),{recursive:true});
 fs.writeFileSync(path.join(folder,'publish/runtimes/linux-x64/native/libfixture.so'),'native-fixture-bytes');
 const zip=`IdentityProvider-${version}.zip`;
 assert.equal(spawnSync('zip',['-q','-r','-X',path.join(folder,'packages',zip),'.'],{cwd:path.join(folder,'publish')}).status,0);
 const manifest={application:'IdentityProvider.Api',version,gitTag:'v'+version,sourceCommit:'a'.repeat(40),sourceTree:'b'.repeat(40),buildNumber:'1',zipFile:zip,zipSha256:sha(fs.readFileSync(path.join(folder,'packages',zip))),imageRepository:'localhost:5500/identityprovider/api',imageDigest:'sha256:'+'c'.repeat(64),createdAtUtc:'2026-01-01T00:00:00.000Z'};
 writeJson(path.join(folder,'manifest.json'),manifest);fs.writeFileSync(path.join(folder,'CHANGELOG.md'),'# Fixture release');fs.writeFileSync(path.join(folder,'release-notes.md'),'Fixture notes');fs.writeFileSync(path.join(folder,'SHA256SUMS'),inventory(folder));
 writeJson(path.join(process.env.ARTIFACT_ROOT,'published',version+'.json'),{version,sourceCommit:manifest.sourceCommit,imageDigest:manifest.imageDigest,finalizedAtUtc:'2026-01-01T00:00:01.000Z'});
 return folder;
}
test('Nexus rejects URL credentials, remote plaintext HTTP and unsafe repository identifiers',()=>{
 const env={NEXUS_BASE_URL:'https://nexus.example.test',NEXUS_REPOSITORY:'project-releases',NEXUS_USERNAME:'reader',NEXUS_PASSWORD:'fixture'};
 assert.equal(nexusSettings(env).repository,'project-releases');
 for(const change of [{NEXUS_BASE_URL:'https://user:password@nexus.example.test'},{NEXUS_BASE_URL:'http://remote.example.test'},{NEXUS_REPOSITORY:'../escape'},{NEXUS_PASSWORD:''}])assert.throws(()=>nexusSettings({...env,...change}));
});
test('Nexus authentication and publisher/reader separation are enforced over real HTTP',async()=>{
 await client().putOnce('probe.txt',Buffer.from('value'));
 assert.equal((await client('reader').get('probe.txt')).toString(),'value');
 await assert.rejects(client('reader').putOnce('new.txt',Buffer.from('blocked')),/403/);
 await assert.rejects(nexusClient({NEXUS_BASE_URL:base,NEXUS_REPOSITORY:'test-releases',NEXUS_USERNAME:'bad',NEXUS_PASSWORD:'fixture'}).get('probe.txt'),/401/);
});
test('Retrying identical immutable assets does not upload again; changed bytes are rejected',async()=>{
 await client().putOnce('probe.txt',Buffer.from('original'));const count=writes.length;
 assert.equal(await client().putOnce('probe.txt',Buffer.from('original')),'verified-existing');assert.equal(writes.length,count);
 await assert.rejects(client().putOnce('probe.txt',Buffer.from('changed')),/collision/);assert.equal(objects.get('probe.txt').toString(),'original');
});
test('Nexus authentication is never followed through an HTTP redirect',async()=>{
 redirectAt='redirect.txt';await assert.rejects(client().get('redirect.txt'));
});
test('Nexus assets cannot escape their repository using path traversal',async()=>{
 for(const name of ['../escape','releases/../../escape','/absolute','a//b','a/%2fsecret'])await assert.rejects(client().get(name),/Unsafe/);
});
test('Paginated listings return finalized versions only and reject repeated page tokens',async()=>{
 objects.set('published/2.0.0.json',Buffer.from('{}'));objects.set('releases/3.0.0/READY.json',Buffer.from('{}'));objects.set('published/1.0.1.json',Buffer.from('{}'));objects.set('published/not-version.json',Buffer.from('{}'));
 assert.deepEqual(await client().versions(),['1.0.1','2.0.0']);leadingSlash=true;assert.deepEqual(await client().versions(),['1.0.1','2.0.0']);paginationLoop=true;await assert.rejects(client().versions(),/pagination/);
});
test('A retained ZIP reconstructs a cold cache byte-for-byte without rebuilding',{skip:!tools},async()=>{
 const folder=release();const original=verifyRelease(folder);const checksums=fs.readFileSync(path.join(folder,'SHA256SUMS'),'utf8');
 await prepareNexusRelease('1.2.3',folder,client());assert.equal(writes.at(-1),'releases/1.2.3/READY.json');
 await finalizeNexusRelease('1.2.3',client());
 process.env.ARTIFACT_ROOT=fs.mkdtempSync(path.join(root,'cold-'));
 const restored=await ensureNexusRelease('1.2.3',client('reader'));assert.deepEqual(restored,original);
 assert.equal(fs.readFileSync(path.join(process.env.ARTIFACT_ROOT,'releases/1.2.3/SHA256SUMS'),'utf8'),checksums);
 assert.deepEqual(await syncNexusReleases(client('reader')),['1.2.3']);
});
test('Interrupted uploads cannot finalize a partial release; retries resume without overwrite',{skip:!tools},async()=>{
 const folder=release();failAt='releases/1.2.3/manifest.json';
 await assert.rejects(prepareNexusRelease('1.2.3',folder,client()),/503/);assert(!objects.has('releases/1.2.3/READY.json'));
 await assert.rejects(finalizeNexusRelease('1.2.3',client()),/404/);
 failAt='';await prepareNexusRelease('1.2.3',folder,client());await finalizeNexusRelease('1.2.3',client());
 assert.equal(writes.filter(p=>p==='releases/1.2.3/CHANGELOG.md').length,1);
});
test('Prepared payloads without a published marker are not consumable',{skip:!tools},async()=>{
 const folder=release();await prepareNexusRelease('1.2.3',folder,client());await assert.rejects(ensureNexusRelease('1.2.3',client('reader')),/404/);
});
test('Corrupted downloaded assets fail before a cold cache is committed',{skip:!tools},async()=>{
 const folder=release();await prepareNexusRelease('1.2.3',folder,client());await finalizeNexusRelease('1.2.3',client());
 objects.set('releases/1.2.3/packages/IdentityProvider-1.2.3.zip',Buffer.from('corrupted'));
 process.env.ARTIFACT_ROOT=fs.mkdtempSync(path.join(root,'corrupt-cold-'));
 await assert.rejects(ensureNexusRelease('1.2.3',client('reader')),/checksum/);assert(!fs.existsSync(path.join(process.env.ARTIFACT_ROOT,'releases/1.2.3')));
});
test('A modified retained local cache is rejected and never silently overwritten',{skip:!tools},async()=>{
 const folder=release();await prepareNexusRelease('1.2.3',folder,client());await finalizeNexusRelease('1.2.3',client());
 fs.appendFileSync(path.join(folder,'publish/IdentityProvider.Api.dll'),'tampered');
 await assert.rejects(ensureNexusRelease('1.2.3',client('reader')),/inventory/);assert(fs.readFileSync(path.join(folder,'publish/IdentityProvider.Api.dll'),'utf8').includes('tampered'));
});
test('A valid inventory cannot conceal a ZIP containing different publish bytes',{skip:!tools},async()=>{
 const folder=release();fs.writeFileSync(path.join(folder,'publish/IdentityProvider.Api.dll'),'different-publish');fs.writeFileSync(path.join(folder,'SHA256SUMS'),inventory(folder));
 await assert.rejects(prepareNexusRelease('1.2.3',folder,client()),/inventory/);assert.equal(writes.length,0);
});
test('Remote metadata cannot inject additional or traversing files into a cold cache',{skip:!tools},async()=>{
 const folder=release();await prepareNexusRelease('1.2.3',folder,client());await finalizeNexusRelease('1.2.3',client());
 const ready=JSON.parse(objects.get('releases/1.2.3/READY.json'));ready.files[0].path='../escape';objects.set('releases/1.2.3/READY.json',Buffer.from(JSON.stringify(ready)));
 process.env.ARTIFACT_ROOT=fs.mkdtempSync(path.join(root,'unsafe-cold-'));await assert.rejects(ensureNexusRelease('1.2.3',client('reader')),/file set/);
});
test('ZIP symlinks are rejected before extraction',{skip:!tools||process.platform==='win32'},()=>{
 const folder=fs.mkdtempSync(path.join(root,'symlink-'));fs.symlinkSync('/tmp/outside',path.join(folder,'link'));const zip=path.join(folder,'symlink.zip');
 assert.equal(spawnSync('zip',['-q','-y',zip,'link'],{cwd:folder}).status,0);assert.throws(()=>extractPublish(zip,path.join(folder,'publish')),/symlinks/);assert(!fs.existsSync(path.join(folder,'publish')));
});
