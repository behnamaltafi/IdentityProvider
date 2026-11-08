// RETAINED LEGACY LOCAL-LAB SEED. New GitHub jobs use infra/platform/casc/jobs.groovy.
import jenkins.model.Jenkins
import hudson.plugins.git.GitSCM
import hudson.plugins.git.BranchSpec
import jenkins.plugins.git.GitSCMSource
import jenkins.plugins.git.traits.BranchDiscoveryTrait
import jenkins.scm.impl.trait.WildcardSCMHeadFilterTrait
import jenkins.branch.BranchSource
import org.jenkinsci.plugins.workflow.multibranch.WorkflowMultiBranchProject
import org.jenkinsci.plugins.workflow.multibranch.WorkflowBranchProjectFactory
import org.jenkinsci.plugins.workflow.job.WorkflowJob
import org.jenkinsci.plugins.workflow.cps.CpsScmFlowDefinition
import com.cloudbees.hudson.plugins.folder.computed.PeriodicFolderTrigger
import hudson.model.User
import hudson.slaves.EnvironmentVariablesNodeProperty
import hudson.security.HudsonPrivateSecurityRealm

// Idempotent additive seed: never changes the security realm, users, or existing jobs.
def instance = Jenkins.get()
// Default approval lists only to real existing administrator accounts. Preserve
// an administrator's explicit lists, and never invent identities or credentials.
def administrators = User.getAll().findAll { user ->
    user.getProperty(HudsonPrivateSecurityRealm.Details) != null &&
        instance.getACL().hasPermission2(user.impersonate2(), Jenkins.ADMINISTER)
}.collect { it.id }.sort().join(',')
if (administrators) {
    def property = instance.globalNodeProperties.get(EnvironmentVariablesNodeProperty)
    if (property == null) {
        property = new EnvironmentVariablesNodeProperty()
        instance.globalNodeProperties.add(property)
    }
    ['BONAK_RELEASE_APPROVERS','BONAK_PRODUCTION_APPROVERS'].each { key ->
        if (!property.envVars.get(key)?.trim()) property.envVars.put(key, administrators)
    }
    instance.save()
}
def node = instance.getNode('linux-agent')
if (node == null || node.numExecutors != 1) throw new IllegalStateException('Register linux-agent with one executor first')
def url = 'file:///git/IdentityProvider.git'
def name = 'IdentityProvider-Branches'
if (instance.getItem(name) == null) {
    def job = instance.createProject(WorkflowMultiBranchProject, name)
    def source = new GitSCMSource(url)
    source.traits = [new BranchDiscoveryTrait(), new WildcardSCMHeadFilterTrait('master develop feature/* bugfix/* release/* hotfix/*', '')]
    job.sourcesList.add(new BranchSource(source))
    def factory = new WorkflowBranchProjectFactory()
    factory.scriptPath = 'Jenkinsfile'
    job.projectFactory = factory
    job.description = 'Full-history local Git Flow branch CI. No stable publication. Poll/index manually; no external webhooks.'
    job.save()
}
def branchJob = instance.getItem(name)
if (branchJob.getTriggers().values().find { it instanceof PeriodicFolderTrigger } == null) {
    branchJob.addTrigger(new PeriodicFolderTrigger('2m'))
    branchJob.save()
}
branchJob.scheduleBuild2(0)
[
    'IdentityProvider-Stable': 'jenkins/Jenkinsfile.release',
    'IdentityProvider-Promote': 'jenkins/Jenkinsfile.promote',
    'IdentityProvider-Rollback': 'jenkins/Jenkinsfile.rollback'
].each { jobName, script ->
    if (instance.getItem(jobName) == null) {
        def job = instance.createProject(WorkflowJob, jobName)
        def scm = new GitSCM(url)
        scm.branches = [new BranchSpec('*/master')]
        def definition = new CpsScmFlowDefinition(scm, script)
        definition.lightweight = true
        job.definition = definition
        job.description = 'Trusted master only. Manual trigger. Stable publication and Production require explicit restricted input.'
        job.save()
    }
}
println('IdentityProvider additive jobs seed completed; existing jobs and security settings preserved.')
