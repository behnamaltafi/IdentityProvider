FROM jenkins/jenkins:lts-jdk21@sha256:74401de6a6f92e1afe1b02d1c8e3d0c33f263bfb5275dd8813f9150355f2367b
COPY --chown=jenkins:jenkins plugins/*.jpi /usr/share/jenkins/ref/plugins/
COPY --chown=jenkins:jenkins plugins.sha256 /usr/share/jenkins/ref/plugins.sha256
RUN cd /usr/share/jenkins/ref/plugins && sha256sum -c ../plugins.sha256
ENV CASC_JENKINS_CONFIG=/opt/platform/casc/jenkins.yaml
USER jenkins
