[CmdletBinding(SupportsShouldProcess)]
param([string]$Version, [string]$Remote = 'origin')
throw 'Manual baseline tagging is retired. No tags are created; the future semantic-release pipeline will own release tags.'
