[CmdletBinding(SupportsShouldProcess)]
param([string]$ArtifactsDirectory = 'artifacts', [string]$Remote = 'origin')
throw 'Publication is disabled during repository preparation. A separately reviewed semantic-release pipeline is required.'
