// Run only in a disposable candidate controller, after JCasC init.
import jenkins.model.Jenkins
import groovy.json.JsonOutput
import org.codehaus.groovy.control.CompilerConfiguration
import org.jenkinsci.plugins.workflow.cps.CpsScript

def j = Jenkins.get()
assert j.numExecutors == 0
assert !j.securityRealm.allowsSignup()
assert j.getNode('ci-agent').numExecutors == 1
assert j.getNode('release-agent').numExecutors == 1
assert !j.getNode('ci-agent').labelString.contains('docker')
assert j.getItemByFullName('identityprovider/branches') != null
assert j.getItemByFullName('identityprovider/release') != null
assert j.pluginManager.failedPlugins.isEmpty()
def compiler = new CompilerConfiguration()
compiler.scriptBaseClass = CpsScript.name
def loader = new GroovyClassLoader(j.pluginManager.uberClassLoader, compiler)
loader.parseClass(new File('/opt/platform/library/vars/dotnetPipeline.groovy'))
// Parse all application entry points with Jenkins' own Groovy classpath.
['Jenkinsfile','jenkins/Jenkinsfile.release','jenkins/Jenkinsfile.promote','jenkins/Jenkinsfile.rollback'].each { name ->
    // Library annotation requires a real GitHub checkout; compile the delegate body here.
    def body = new File('/opt/application/' + name).readLines().findAll { !it.startsWith('@Library') }.join('\n')
    loader.parseClass(body)
}
def multibranch = j.getItemByFullName('identityprovider/branches')
def source = multibranch.SCMSources[0]
assert source.class.name.contains('GitHubSCMSource')
assert source.repoOwner == 'behnamaltafi'
assert source.repository == 'IdentityProvider'
assert source.traits.any { it.class.simpleName == 'OriginPullRequestDiscoveryTrait' }
assert !source.traits.any { it.class.simpleName == 'ForkPullRequestDiscoveryTrait' }
assert source.traits.any { it.class.simpleName == 'BranchDiscoveryTrait' && it.strategyId == 1 }
def result = [controller: Jenkins.VERSION, plugins: j.pluginManager.plugins.size(),
    jcasc: 'loaded', jobs: ['branches','release','promote','rollback'],
    githubSource: source.class.name, sharedLibraryGroovy: 'compiled',
    failedPlugins: [], ciHasDockerLabel: false]
new File('/tmp/platform-validation.json').text = JsonOutput.prettyPrint(JsonOutput.toJson(result))
println('PLATFORM_CONFIGURATION_VALIDATED')
