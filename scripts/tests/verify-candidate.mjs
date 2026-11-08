// Verifies real archived CI ZIP/image bytes without running or deploying the application.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { inventory, must, readJson, registryManifest, run, sha, writeJson } from '../release/lib.mjs';
const directory=path.resolve(process.argv[2]);
const m=readJson(path.join(directory,'metadata.json'));
must(m.official===false&&m.kind==='disposable-ci-candidate','Only explicitly disposable candidates belong to this check');
const zip=path.join(directory,m.zipFile);
must(path.basename(m.zipFile)===m.zipFile&&sha(fs.readFileSync(zip))===m.zipSha256,'Candidate ZIP checksum mismatch');
await registryManifest(m.imageRepository,m.imageDigest);
const scratch=fs.mkdtempSync(path.join(os.tmpdir(),'bonak-bytes-'));
const unpacked=path.join(scratch,'zip'),image=path.join(scratch,'image');fs.mkdirSync(unpacked);fs.mkdirSync(image);
run('unzip',['-q',zip,'-d',unpacked]);
const container=run('docker',['create','--entrypoint','/bin/true',`${m.imageRepository}@${m.imageDigest}`]);
try {
  run('docker',['cp',`${container}:/app/.`,image]);
  must(inventory(unpacked)===inventory(image),'ZIP and packaged image application files differ');
  writeJson(path.join(directory,'byte-verification.json'),{sourceCommit:m.sourceCommit,zipSha256:m.zipSha256,imageDigest:m.imageDigest,identicalApplicationBytes:true,verifiedAtUtc:new Date().toISOString()});
  console.log(`PASS ZIP checksum and byte-for-byte ZIP/image equality for ${m.imageDigest}`);
} finally { run('docker',['rm',container]); }
