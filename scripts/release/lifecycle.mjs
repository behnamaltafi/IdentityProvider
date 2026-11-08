import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import project from './project.cjs';
import {nexusEnabled, prepareNexusRelease, finalizeNexusRelease} from '../artifacts/nexus.mjs';
import { artifactRoot, filesIn, git, inventory, masterGuard, must, readJson, registryManifest, releasePath, run, sha, verifyRelease, versionPattern, writeJson } from './lib.mjs';
const [phase, version] = process.argv.slice(2);
must(['prepare', 'finalize', 'recover'].includes(phase) && versionPattern.test(version), 'Expected prepare/finalize/recover MAJOR.MINOR.PATCH');
must((process.env.CICD_ALLOW_PUBLISH ?? process.env.BONAK_ALLOW_PUBLISH) === 'explicitly-approved', 'Release approval required');
masterGuard();
const root = artifactRoot();
const stored = releasePath(version);
if (phase === 'prepare') {
  const context = readJson('artifacts/release-context.json');
  must(process.env.JENKINS_URL && process.env.BUILD_URL && /^\d+$/.test(process.env.BUILD_NUMBER ?? ''), 'Missing actual Jenkins release build metadata');
  must(context.version === version && context.sourceCommit === git('rev-parse', 'HEAD'), 'Semantic-release context mismatch');
  if (fs.existsSync(stored)) {
    const m = verifyRelease(stored);
    must(m.sourceCommit === context.sourceCommit, 'Immutable artifact collision');
    await registryManifest(m.imageRepository, m.imageDigest);
    fs.cpSync(stored, 'dist', { recursive: true, force: true });
    if (nexusEnabled()) await prepareNexusRelease(version,stored);
    console.error('Reused prepared immutable artifact; no build, publish or image push repeated.');
    process.exit(0);
  }
  const repository = process.env.IMAGE_REPOSITORY ?? project.imageRepository;
  must(!await registryManifest(repository, version, false), 'Version image tag already exists without a matching stored manifest; reconcile manually, never overwrite');
  const commit = git('rev-parse', 'HEAD');
  // This is the single official solution build/test, using semantic-release's exact version.
  run('pwsh', ['-NoProfile', '-File', 'scripts/ci/Invoke-CI.ps1'], { echo: true, env: { ...process.env, RELEASE_VERSION: version } });
  const props = [`-p:Version=${version}`, `-p:PackageVersion=${version}`, `-p:AssemblyVersion=${version}.0`,
    `-p:FileVersion=${version}.0`, `-p:InformationalVersion=${version}+${commit}`,
    '-p:ContinuousIntegrationBuild=true', '-p:IncludeSourceRevisionInInformationalVersion=false'];
  run('dotnet', ['publish', project.publishProject, '-c', 'Release', '--no-build', '--no-restore', '-o', path.resolve('dist/publish'), ...props], { echo: true });
  must(fs.existsSync(path.join('dist/publish', project.publishAssembly)), 'Application publish output missing');
  fs.mkdirSync('dist/packages', { recursive: true });
  const zipFile = `${project.zipPrefix}-${version}.zip`;
  const zipPath = path.resolve('dist/packages', zipFile);
  run('zip', ['-q', '-r', '-X', zipPath, '.'], { cwd: path.resolve('dist/publish') });
  run('unzip', ['-tq', zipPath]);
  // Docker only copies the very same publish directory; it performs no .NET build.
  const image = `${repository}:${version}`;
  run('docker', ['build', '-f', path.resolve('Dockerfile.release'), '-t', image, '--label', `org.opencontainers.image.revision=${commit}`, '--label', `org.opencontainers.image.version=${version}`, 'dist'], { echo: true });
  // Check again immediately before publication under the cross-job release lock.
  must(!await registryManifest(repository, version, false), 'Image version collision');
  run('docker', ['push', image], { echo: true });
  const imageDigest = await registryManifest(repository, version);
  await registryManifest(repository, imageDigest);
  const localDigests = JSON.parse(run('docker', ['image', 'inspect', image, '--format', '{{json .RepoDigests}}']));
  must(localDigests.includes(`${repository}@${imageDigest}`), 'Daemon and Registry image digests disagree');
  writeJson('dist/manifest.json', {
    application: project.application, version, gitTag: `v${version}`, sourceCommit: commit,
    sourceTree: git('rev-parse', 'HEAD^{tree}'), buildNumber: process.env.BUILD_NUMBER,
    buildUrl: process.env.BUILD_URL ?? null, zipFile, zipSha256: sha(fs.readFileSync(zipPath)),
    imageRepository: repository, imageDigest, createdAtUtc: new Date().toISOString()
  });
  fs.writeFileSync('dist/SHA256SUMS', inventory('dist'));
  verifyRelease('dist');
  fs.mkdirSync(path.dirname(stored), { recursive: true });
  const stage = path.join(path.dirname(stored), `.staging-${version}-${crypto.randomUUID()}`);
  fs.cpSync('dist', stage, { recursive: true, errorOnExist: true, force: false });
  verifyRelease(stage);
  must(!fs.existsSync(stored), 'Immutable storage collision');
  fs.renameSync(stage, stored); // Atomic visibility on the persistent artifact filesystem.
  for (const file of filesIn(stored)) fs.chmodSync(path.join(stored, file), 0o444);
  if (nexusEnabled()) await prepareNexusRelease(version,stored);
  console.error(`Prepared ${version} at ${stored}; semantic-release alone creates the official tag next.`);
} else {
  // Also supports tag-pushed / publish-hook-failed recovery, without a new release or rebuild.
  const m = verifyRelease(stored);
  must(m.sourceCommit === git('rev-parse', 'HEAD'), 'Recovery checkout differs from the release source');
  const tagged = git('ls-remote', 'origin', `refs/tags/${m.gitTag}`);
  must(tagged.split(/\s+/)[0] === m.sourceCommit, 'Official remote semantic-release tag is absent or points elsewhere');
  await registryManifest(m.imageRepository, m.imageDigest);
  const marker = path.join(root, 'published', `${version}.json`);
  const record = { version, sourceCommit: m.sourceCommit, imageDigest: m.imageDigest, finalizedAtUtc: new Date().toISOString() };
  if (fs.existsSync(marker)) {
    const old = readJson(marker);
    must(old.sourceCommit === record.sourceCommit && old.imageDigest === record.imageDigest, 'Finalization collision');
  } else writeJson(marker, record, true);
  if (nexusEnabled()) await finalizeNexusRelease(version);
  console.log(JSON.stringify({ name: `${project.application} ${version}`, url: `file://${stored}` }));
}
