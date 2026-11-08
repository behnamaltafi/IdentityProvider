// Supported Nexus REST provisioning. Credentials are read/written only to caller-selected local files.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const [mode,baseUrl,adminFile,projectsFile,secretsDirectory]=process.argv.slice(2);
if(!['plan','apply'].includes(mode)||!baseUrl||!adminFile||!projectsFile||!secretsDirectory)throw new Error('Use plan|apply BASE_URL ADMIN_JSON PROJECTS_JSON SECRETS_DIRECTORY');
let url;try{url=new URL(baseUrl);}catch{throw new Error('Invalid credential-free Nexus URL');}
if(url.username||url.password||url.search||url.hash||!(url.protocol==='https:'||(url.protocol==='http:'&&['localhost','127.0.0.1','nexus'].includes(url.hostname))))throw new Error('Use a credential-free HTTPS URL or the local lab endpoint');
let admin;try{admin=JSON.parse(fs.readFileSync(adminFile,'utf8').replace(/^\uFEFF/,''));}catch{throw new Error('Invalid Nexus admin credential file; values suppressed');}
if(typeof admin.username!=='string'||!admin.username||admin.username.includes(':')||typeof admin.password!=='string'||!admin.password)throw new Error('Nexus admin credential file is incomplete');
const projects=JSON.parse(fs.readFileSync(projectsFile,'utf8').replace(/^\uFEFF/,''));
const output=path.resolve(secretsDirectory);
const approved=process.env.NEXUS_CREDENTIAL_CHANGES_APPROVED==='yes';
const safe=name=>typeof name==='string'&&/^[a-z0-9][a-z0-9-]{0,63}$/.test(name);
for(const p of projects)if(![p.project,p.repository,p.publisherUser,p.readerUser].every(safe))throw new Error('Invalid project/repository/user identifier');
async function request(endpoint,{method='GET',body,plain=false,optional=false}={}) {
 const response=await fetch(url.href.replace(/\/+$/,'')+'/service/rest'+endpoint,{method,headers:{Authorization:'Basic '+Buffer.from(admin.username+':'+admin.password).toString('base64'),...(body!==undefined?{'Content-Type':plain?'text/plain':'application/json'}:{})},body:body===undefined?undefined:plain?body:JSON.stringify(body),redirect:'error',signal:AbortSignal.timeout(60000)});
 if(response.status===404&&optional){await response.body?.cancel();return null;}
 if(!response.ok){await response.body?.cancel();throw new Error(`Nexus provisioning ${method} ${endpoint} HTTP ${response.status}; sensitive response suppressed`);}
 const text=await response.text();return text?JSON.parse(text):null;
}
const repositories=await request('/v1/repositories');
const roles=await request('/v1/security/roles');
const users=await request('/v1/security/users');
const telemetry=await request('/v1/system/mandatory-telemetry-acknowledgement',{optional:true});
const changes=projects.map(p=>({project:p.project,repository:p.repository,writePolicy:'ALLOW_ONCE',publisherUser:p.publisherUser,readerUser:p.readerUser,existingRepository:repositories.some(r=>r.name===p.repository),publisherCredentialId:p.publisherCredentialId,readerCredentialId:p.readerCredentialId}));
if(mode==='plan'){
 console.log(JSON.stringify({mode,telemetry,anonymousAccess:await request('/v1/security/anonymous'),changes,credentialChangesRequireApproval:true},null,2));process.exit(0);
}
if(!approved)throw new Error('Credential changes require explicit approval; use plan first');
if(telemetry?.acknowledgement!==true){
 if(process.env.NEXUS_ACKNOWLEDGE_REQUIRED_TELEMETRY!=='yes')throw new Error('Required Nexus telemetry acknowledgement needs explicit user approval; no mutation performed');
 await request('/v1/system/mandatory-telemetry-acknowledgement',{method:'POST',body:{acknowledgement:true}});
 const acknowledged=await request('/v1/system/mandatory-telemetry-acknowledgement');
 if(acknowledged?.acknowledgement!==true)throw new Error('Nexus telemetry acknowledgement was not verified');
}
if(process.env.NEXUS_ROTATE_BOOTSTRAP_ADMIN==='yes'){
 if(admin.username!=='admin')throw new Error('Bootstrap rotation only applies to the admin account');
 fs.mkdirSync(output,{recursive:true});
 const destination=path.join(output,'nexus-admin.json');
 if(fs.existsSync(destination))throw new Error('Final admin credential file already exists; refusing to replace it');
 const replacement={username:'admin',password:'aA1!'+crypto.randomBytes(32).toString('base64url')};
 // Persist the recovery credential before the server operation, whose response could be lost.
 fs.writeFileSync(destination,JSON.stringify(replacement)+'\n',{mode:0o600,flag:'wx'});
 await request('/v1/security/users/admin/change-password',{method:'PUT',body:replacement.password,plain:true});
 admin=replacement;await request('/v1/security/users');
}
const anonymous=await request('/v1/security/anonymous');
if(anonymous.enabled)await request('/v1/security/anonymous',{method:'PUT',body:{...anonymous,enabled:false}});
const result=[];
for(const p of projects){
 const existing=repositories.find(r=>r.name===p.repository);
 if(existing){
  if(existing.format!=='raw'||existing.type!=='hosted')throw new Error('Existing Nexus repository has a different format/type');
  const detail=await request('/v1/repositories/raw/hosted/'+p.repository);
  if(detail.storage.writePolicy!=='ALLOW_ONCE'||!detail.online)throw new Error('Existing repository is not immutable/online; refusing to change its policy');
 }else await request('/v1/repositories/raw/hosted',{method:'POST',body:{name:p.repository,online:true,storage:{blobStoreName:'default',strictContentTypeValidation:false,writePolicy:'ALLOW_ONCE'}}});
 for(const [access,user,actions,credentialId]of [
  ['publish',p.publisherUser,['browse','read','add','edit'],p.publisherCredentialId],
  ['read',p.readerUser,['browse','read'],p.readerCredentialId]
 ]){
  const id=`cicd-${p.project}-${access}`;
  const privileges=actions.map(a=>`nx-repository-view-raw-${p.repository}-${a}`);
  const role=roles.find(r=>r.id===id);
  if(role){if(JSON.stringify([...role.privileges].sort())!==JSON.stringify([...privileges].sort())||role.roles.length)throw new Error('Existing Nexus role differs; refusing to broaden or overwrite it');}
  else await request('/v1/security/roles',{method:'POST',body:{id,name:id,description:`${p.project} immutable release ${access}`,privileges,roles:[]}});
  const file=path.join(output,`nexus-${p.project}-${access}.json`);
  const found=users.find(u=>u.userId===user);
  if(found){
   if(found.status!=='active'||JSON.stringify([...found.roles].sort())!==JSON.stringify([id])||!fs.existsSync(file))throw new Error('Existing service user cannot be safely reused; no password reset is performed');
  }else{
   if(fs.existsSync(file))throw new Error('Service credential file exists without its Nexus user; reconcile before retrying');
   const password='aA1!'+crypto.randomBytes(32).toString('base64url');
   fs.mkdirSync(output,{recursive:true});fs.writeFileSync(file,JSON.stringify({username:user,password})+'\n',{mode:0o600,flag:'wx'});
   await request('/v1/security/users',{method:'POST',body:{userId:user,firstName:'CI',lastName:access,emailAddress:`${user}@localhost.invalid`,password,status:'active',roles:[id]}});
  }
  result.push({project:p.project,repository:p.repository,user,role:id,credentialId,credentialFile:file});
 }
}
console.log(JSON.stringify({mode,result,anonymousAccess:false,immutableRepositories:true},null,2));
