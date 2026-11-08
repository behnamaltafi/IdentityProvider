[CmdletBinding()]
param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference = 'Stop'
foreach ($trx in Get-ChildItem $Root -Recurse -Filter tests.trx) {
    [xml]$source = Get-Content $trx.FullName -Raw
    $manager = [Xml.XmlNamespaceManager]::new($source.NameTable)
    $manager.AddNamespace('t', $source.DocumentElement.NamespaceURI)
    $results = @($source.SelectNodes('//t:UnitTestResult', $manager))
    if (-not $results.Count) { throw "TRX contains no real test results: $($trx.FullName)" }
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $writer = [Xml.XmlWriter]::Create((Join-Path $trx.DirectoryName 'junit.xml'), $settings)
    try {
        $writer.WriteStartElement('testsuite')
        $writer.WriteAttributeString('name', $trx.Directory.Parent.Name + '.' + $trx.Directory.Name)
        $writer.WriteAttributeString('tests', "$($results.Count)")
        $writer.WriteAttributeString('failures', "$(@($results | Where-Object outcome -eq 'Failed').Count)")
        foreach ($result in $results) {
            $writer.WriteStartElement('testcase')
            $writer.WriteAttributeString('name', $result.testName)
            $writer.WriteAttributeString('classname', $trx.Directory.Name)
            $duration = [TimeSpan]::Parse($result.duration, [Globalization.CultureInfo]::InvariantCulture).TotalSeconds
            $writer.WriteAttributeString('time', $duration.ToString([Globalization.CultureInfo]::InvariantCulture))
            if ($result.outcome -eq 'Failed') {
                $writer.WriteStartElement('failure')
                $writer.WriteString($result.InnerText)
                $writer.WriteEndElement()
            } elseif ($result.outcome -ne 'Passed') { $writer.WriteElementString('skipped', $result.outcome) }
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
    } finally { $writer.Dispose() }
}
