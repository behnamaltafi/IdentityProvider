// The library must be pinned to an administrator-reviewed commit in JCasC.
// CI runs without Docker, release volumes or release credentials.
def call(Map settings = [:]) {
    def mode = settings.mode ?: 'ci'
    if (!(mode in ['ci', 'release', 'promote', 'rollback'])) error('Unsupported pipeline mode')
    def config
    pipeline {
        agent { label "${mode == 'ci' ? (settings.ciLabel ?: 'linux && dotnet10 && ci') : (settings.releaseLabel ?: 'linux && docker && dotnet10 && release')}" }
        options {
            skipDefaultCheckout(); disableConcurrentBuilds(); timestamps()
            buildDiscarder(logRotator(numToKeepStr: '50', artifactNumToKeepStr: '50'))
        }
        parameters {
            booleanParam(name: 'PUBLISH', defaultValue: false, description: 'Request an official release, subject to human approval')
            booleanParam(name: 'PROMOTE', defaultValue: false, description: 'Promote the published artifact through environments')
            string(name: 'VERSION', defaultValue: '', description: 'Exact retained release version for promotion/rollback')
            string(name: 'TARGET', defaultValue: 'production', description: 'Configured deployment environment')
        }
        stages {
            stage('Checkout and configuration') {
                steps { script {
                    deleteDir()
                    def metadata = checkout scm
                    env.BRANCH_NAME = env.BRANCH_NAME ?: metadata.GIT_BRANCH.replaceFirst('^origin/', '')
                    if (mode != 'ci' && (env.BRANCH_NAME != 'master' || env.CHANGE_ID)) error('Trusted master is required')
                    sh 'test "$(git rev-parse --is-shallow-repository)" = false'
                    config = readJSON(file: settings.configuration ?: 'cicd/project.json', returnPojo: true)
                    if (!(config.id ==~ /[a-z0-9][a-z0-9-]*/)) error('Invalid project id')
                    if (sh(script: 'git remote get-url origin', returnStdout: true).trim() != config.repositoryUrl) error('Origin differs from configured authoritative GitHub URL')
                    sh 'npm ci --ignore-scripts --no-audit --no-fund'
                    sh 'npm run test:release && npm run test:pipeline && npm run test:nexus --if-present'
                } }
            }
            stage('Branch and PR validation') {
                when { expression { mode == 'ci' } }
                steps { script { sh config.commands.ci } }
            }
            stage('Release or retained artifact operation') {
                when { expression { mode != 'ci' } }
                steps { script {
                    withEnv([
                        "ARTIFACT_ROOT=${config.artifactRoot}", "ENVIRONMENT_ROOT=${config.environmentRoot}",
                        "IMAGE_REPOSITORY=${env.IMAGE_REPOSITORY ?: config.imageRepository}",
                        "REGISTRY_API_URL=${env.REGISTRY_API_URL ?: config.registryApiUrl}"
                    ]) {
                        lock(resource: "${config.id}-release-and-deployment") {
                            if (mode == 'release') {
                                // Private repository auth is scoped to this trusted master stage.
                                githubCredentials(config) { sh config.commands.preview }
                                    archiveArtifacts artifacts: 'artifacts/release-plan.json', fingerprint: true
                                    def plan = readJSON(file: 'artifacts/release-plan.json', returnPojo: true)
                                    if (params.PUBLISH && plan.release) {
                                        if (!env.CICD_RELEASE_APPROVERS?.trim()) error('Set restricted release approvers')
                                        timeout(time: 24, unit: 'HOURS') {
                                            input message: "Publish ${config.id} ${plan.release.version} to GitHub and immutable storage?", submitter: env.CICD_RELEASE_APPROVERS
                                        }
                                        githubCredentials(config) {
                                            artifactCredentials(config, 'publish') {
                                                withEnv(['CICD_ALLOW_PUBLISH=explicitly-approved']) { sh config.commands.publish }
                                            }
                                        }
                                        if (params.PROMOTE) promote(config, plan.release.version, params.TARGET)
                                    }
                            } else {
                                def version = params.VERSION.trim()
                                def retained = artifactCredentials(config, 'read') { sh(script: config.commands.versions, returnStdout: true) }.trim().readLines()
                                if (!(version ==~ /(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)/) || !retained.contains(version)) error('Select an exact retained finalized version')
                                if (mode == 'promote') promote(config, version, params.TARGET)
                                else deploy(config, version, params.TARGET, 'rollback')
                            }
                        }
                    }
                } }
            }
        }
        post { always {
            junit testResults: 'artifacts/test-results/**/junit.xml', allowEmptyResults: true
            archiveArtifacts artifacts: 'artifacts/**,dist/**', allowEmptyArchive: true, fingerprint: true
        } }
    }
}

def githubCredentials(Map config, Closure body) {
    // Bind again after approval, so GitHub App installation tokens are fresh.
    withCredentials([
        gitUsernamePassword(credentialsId: config.releaseCredentialsId, gitToolName: 'Default'),
        usernamePassword(credentialsId: config.releaseCredentialsId, usernameVariable: 'RELEASE_GITHUB_USER', passwordVariable: 'GH_TOKEN')
    ]) { body() }
}

def artifactCredentials(Map config, String access, Closure body) {
    def store = config.artifactStore
    if (store?.backend != 'nexus') return body()
    def credential = access == 'publish' ? store.publisherCredentialsId : store.readerCredentialsId
    if (!credential?.trim()) error('Configure a scoped Nexus credential')
    withEnv(['ARTIFACT_STORE_BACKEND=nexus', "NEXUS_BASE_URL=${store.baseUrl}", "NEXUS_REPOSITORY=${store.repository}"]) {
        withCredentials([usernamePassword(credentialsId: credential, usernameVariable: 'NEXUS_USERNAME', passwordVariable: 'NEXUS_PASSWORD')]) { body() }
    }
}

def promote(Map config, String version, String target) {
    if (!config.environments.contains(target)) error('Unknown target environment')
    for (def environment : config.environments) {
        deploy(config, version, environment, 'deploy')
        if (environment == target) break
    }
}

def deploy(Map config, String version, String target, String operation) {
    if (!config.environments.contains(target)) error('Unknown target environment')
    withEnv(["RELEASE_VERSION=${version}", "TARGET_ENV=${target}", "CICD_OPERATION=${operation}", 'APPROVAL_ID=']) {
        if (target == 'production') {
            if (!env.CICD_PRODUCTION_APPROVERS?.trim()) error('Set restricted Production approvers')
            def actor
            timeout(time: 24, unit: 'HOURS') {
                actor = input message: "${operation} ${config.id} ${version} to Production using its immutable digest?", ok: 'Approve',
                    submitter: env.CICD_PRODUCTION_APPROVERS, submitterParameter: 'APPROVER'
            }
            def who = actor instanceof Map ? actor.APPROVER : actor.toString()
            withEnv(["CICD_APPROVER=${who}"]) {
                def approval = artifactCredentials(config, 'read') { sh(script: config.commands.approve, returnStdout: true) }.trim()
                withEnv(["APPROVAL_ID=${approval}"]) { artifactCredentials(config, 'read') { sh config.commands[operation] } }
            }
        } else { artifactCredentials(config, 'read') { sh config.commands[operation] } }
    }
}
