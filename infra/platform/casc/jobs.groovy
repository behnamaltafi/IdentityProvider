import groovy.json.JsonSlurper
import java.util.regex.Pattern

// Source-controlled administrator input only. Application jobs cannot edit this directory.
new File('/opt/platform/projects').listFiles().findAll { it.name.endsWith('.json') }.sort { it.name }.each { file ->
    def p = new JsonSlurper().parse(file)
    assert p.id ==~ /[a-z0-9][a-z0-9-]*/
    assert p.owner ==~ /[A-Za-z0-9-]+/
    assert p.repository ==~ /[A-Za-z0-9_.-]+/
    assert p.url == "https://github.com/${p.owner}/${p.repository}.git"
    def bootstrap = p.bootstrapHeads ?: []
    def newHeadsRegex = bootstrap ? '(?!(?:' + bootstrap.collect { Pattern.quote(it) }.join('|') + ')$).+' : '.+'
    folder(p.id)
    multibranchPipelineJob("${p.id}/branches") {
        branchSources {
            branchSource {
                buildStrategies {
                    if (bootstrap) skipInitialBuildOnFirstBranchIndexing()
                    buildChangeRequests { ignoreTargetOnlyChanges(false); ignoreUntrustedChanges(false) }
                    buildNamedBranches { filters { regex { regex(newHeadsRegex); caseSensitive(true) } } }
                }
                source {
                    github {
                        id("${p.id}-github")
                        apiUri('https://api.github.com')
                        credentialsId(p.scanCredentialsId)
                        repoOwner(p.owner)
                        repository(p.repository)
                        repositoryUrl(p.url)
                        configuredByUrl(true)
                        traits {
                            // Exclude branches with open PRs; validate their merge revision once.
                            gitHubBranchDiscovery { strategyId(1) }
                            gitHubPullRequestDiscovery { strategyId(1) }
                            // Fork PRs deliberately disabled on the long-lived agent.
                            headWildcardFilter {
                                includes('master develop feature/* bugfix/* release/* hotfix/* PR-*')
                                excludes('')
                            }
                        }
                    }
                }
            }
        }
        factory { workflowBranchProjectFactory { scriptPath('Jenkinsfile') } }
        triggers { periodicFolderTrigger { interval(p.indexInterval ?: '5m') } }
        orphanedItemStrategy { defaultOrphanedItemStrategy { pruneDeadBranches(false); daysToKeepStr('-1'); numToKeepStr('-1') } }
    }
    ['release': 'jenkins/Jenkinsfile.release', 'promote': 'jenkins/Jenkinsfile.promote', 'rollback': 'jenkins/Jenkinsfile.rollback'].each { kind, script ->
        pipelineJob("${p.id}/${kind}") {
            disabled(!(p.deploymentEnabled ?: false))
            definition {
                cpsScm {
                    scm {
                        git {
                            remote { url(p.url); credentials(p.checkoutCredentialsId) }
                            branch('*/master')
                            extensions { cloneOptions { shallow(false); noTags(false) } }
                        }
                    }
                    scriptPath(script)
                    lightweight(true)
                }
            }
            // Intentionally no SCM trigger: manual publication/deployment only.
        }
    }
}
