#!/bin/bash
set -euo pipefail
: "${JENKINS_AGENT_NAME:?Set agent name}"
test -s /run/secrets/agent-secret
exec /usr/local/bin/jenkins-agent -url "${JENKINS_CONTROLLER_URL:-http://controller:8080/}" \
  -secret @/run/secrets/agent-secret -name "$JENKINS_AGENT_NAME" \
  -webSocket -workDir /home/jenkins/agent
