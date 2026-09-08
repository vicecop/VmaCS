param(
  [Parameter(Mandatory=$true)]
  [string]$Version
)

$ErrorActionPreference = 'Stop'

$projPath = Join-Path $PSScriptRoot "src\VmaCS\VmaCS.csproj"

[xml]$xml = Get-Content $projPath
$ns = New-Object Xml.XmlNamespaceManager $xml.NameTable
$ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
$node = $xml.SelectSingleNode("//msb:Version", $ns)

if ($null -eq $node) {
  throw "Version node not found in $projPath"
}

$node.InnerText = $Version
$xml.Save($projPath)

git add $projPath
git commit -m "chore: bump version to $Version"
git tag "v$Version"
git push origin "v$Version"
