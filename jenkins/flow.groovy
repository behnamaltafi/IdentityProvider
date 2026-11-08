// Retained legacy implementation. GitHub Jenkinsfiles use the pinned platform Shared Library.
def checkoutFull() {
    deleteDir()
    def metadata = checkout scm
    def branch = env.BRANCH_NAME ?: (metadata.GIT_BRANCH ?: '').replaceFirst('^origin/', '')
    if (!(branch ==~ /master|develop|(feature|bugfix|release|hotfix)\/[A-Za-z0-9][A-Za-z0-9._\/-]*/)) error('Unsupported branch metadata')
    env.BRANCH_NAME = branch
    sh 'git fetch origin --tags "+refs/heads/*:refs/remotes/origin/*"; test "$(git rev-parse --is-shallow-repository)" = false'
    sh 'npm ci --no-audit --no-fund'
    sh 'npm run test:release && npm run test:pipeline'
}
def reports() {
    if (sh(script: 'find artifacts/test-results -name junit.xml -print -quit 2>/dev/null | grep -q .', returnStatus: true) == 0) {
        junit testResults: 'artifacts/test-results/**/junit.xml', allowEmptyResults: false
        recordCoverage tools: [[parser: 'COBERTURA', pattern: 'artifacts/test-results/*/IdentityProvider.*Tests/coverage.cobertura.xml']], sourceCodeRetention: 'NEVER'
    }
    archiveArtifacts artifacts: 'artifacts/**,dist/**', allowEmptyArchive: true, fingerprint: true
}
def productionApproval(String version, String operation) {
    def users = env.BONAK_PRODUCTION_APPROVERS
    if (!users?.trim()) error('Administrator must set BONAK_PRODUCTION_APPROVERS; refusing unrestricted approval')
    def answer
    timeout(time: 24, unit: 'HOURS') {
        answer = input message: "${operation} IdentityProvider ${version} to Production using its immutable digest?", ok: 'Approve',
            submitter: users, submitterParameter: 'APPROVER'
    }
    def who = answer instanceof Map ? answer.APPROVER : answer.toString()
    withEnv(["BONAK_APPROVER=${who}", "BONAK_OPERATION=${operation}", "RELEASE_VERSION=${version}"]) {
        return sh(script: 'node scripts/deploy/cli.mjs approve production "$RELEASE_VERSION"', returnStdout: true).trim()
    }
}
def promote(String version, String target) {
    if (!(version ==~ /(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)/)) error('Select an exact retained release version')
    withEnv(["RELEASE_VERSION=${version}"]) {
        lock(resource: 'bonak-deployment-progression') {
            if (target in ['internal','uat','production']) sh 'node scripts/deploy/cli.mjs deploy internal "$RELEASE_VERSION"'
            if (target in ['uat','production']) sh 'node scripts/deploy/cli.mjs deploy uat "$RELEASE_VERSION"'
            if (target == 'production') {
                def approval = productionApproval(version, 'deploy')
                withEnv(["APPROVAL_ID=${approval}"]) { sh 'node scripts/deploy/cli.mjs deploy production "$RELEASE_VERSION" "$APPROVAL_ID"' }
            }
        }
    }
}
return this
