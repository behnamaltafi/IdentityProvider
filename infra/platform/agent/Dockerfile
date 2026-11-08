FROM jenkins/inbound-agent:jdk21@sha256:5069347cd3e72e5924e54c04fceb67209b41c8d0a3ab1c2b3f740e5fb2fab92a AS inbound
FROM node:24-bookworm-slim@sha256:01d566e8d94d8c619591d78652babfccd3c6992a3dffa9ed5ab39e11cf6c70e9 AS node
FROM docker:29.9.2-cli@sha256:5e6baf67325a79f64ed3bed6863954d5e05d0f3d4e57cbdb14809346814bbfdb AS dockercli
FROM docker/compose-bin:v5.6.1@sha256:2aeaeedd4f2d8e2dcb399b2b9723bfaeb6714140cb0657897f5865960f1624ae AS compose
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
      bash ca-certificates curl git jq zip unzip coreutils procps \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 1001 --create-home --shell /bin/bash jenkins \
    && mkdir -p /home/jenkins/agent /release-artifacts /usr/share/jenkins /usr/local/libexec/docker/cli-plugins \
    && chown -R jenkins:jenkins /home/jenkins /release-artifacts
COPY --from=inbound /opt/java/openjdk /opt/java/openjdk
COPY --from=inbound /usr/share/jenkins/agent.jar /usr/share/jenkins/agent.jar
COPY --from=inbound /usr/local/bin/jenkins-agent /usr/local/bin/jenkins-agent
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=dockercli /usr/local/libexec/docker/cli-plugins/docker-buildx /usr/local/libexec/docker/cli-plugins/docker-buildx
COPY --from=compose /docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx \
    && chmod +x /usr/local/libexec/docker/cli-plugins/* \
    && command -v pwsh \
    && dpkg-query -W > /usr/share/jenkins/os-packages.txt
ENV JAVA_HOME=/opt/java/openjdk \
    JENKINS_AGENT_FILE=/usr/share/jenkins/agent.jar \
    AGENT_WORKDIR=/home/jenkins/agent \
    PATH=/opt/java/openjdk/bin:$PATH \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
COPY --chmod=755 entrypoint.sh /usr/local/bin/lab-agent
USER jenkins
WORKDIR /home/jenkins/agent
RUN java -version && dotnet --version && node --version && npm --version \
    && git --version && docker --version && docker compose version && pwsh --version
ENTRYPOINT ["/usr/local/bin/lab-agent"]
