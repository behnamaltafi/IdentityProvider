import { artifactRoot, must, published, run } from '../release/lib.mjs';
import { latest } from '../deploy/deploy.mjs';
const [version]=process.argv.slice(2);
const m=published(version);
for(const environment of ['internal','uat','production']){
  const record=latest(environment,m.imageDigest);must(record?.version===version,`${environment} has no healthy record for selected digest`);
  const containers=JSON.parse(run('docker',['inspect',record.containerId]));
  must(containers[0].State.Health.Status==='healthy'&&containers[0].Config.Image===`${m.imageRepository}@${m.imageDigest}`,`${environment} running health/digest mismatch`);
  if(environment==='production')must(record.approvedBy&&record.approvalId,'Missing actual Production approval trace');
}
console.log(`PASS all three environments use ${m.imageDigest}; evidence under ${artifactRoot()}`);
