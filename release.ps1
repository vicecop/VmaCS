param(
  [Parameter(Mandatory=$true)]
  [string]$Version
)

$ErrorActionPreference = 'Stop'

$projPath = Join-Path $PSScriptRoot "src\VmaCS\VmaCS.csproj"

[xml]$xml = Get-Content $projPath
$ns = New-Object Xml.XmlNamespaceManager $xml.NameTable
$ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")

# Try with namespace first (old-style), then without (SDK-style)
$node = $xml.SelectSingleNode("//msb:Version", $ns)
if ($null -eq $node) {
    $node = $xml.SelectSingleNode("//Version")
}

if ($null -eq $node) {
    throw "Version node not found in $projPath"
}

$node.InnerText = $Version
$xml.Save($projPath)

# Build and test locally before pushing
Write-Host "Building..."
dotnet build $projPath -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "Running unit tests..."
dotnet test test/VmaCS.Tests.Unit/VmaCS.Tests.Unit.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed" }

Write-Host "Running ported tests..."
dotnet test test/VmaCS.Tests.Ported/VmaCS.Tests.Ported.csproj -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Ported tests failed" }

git add $projPath
git commit -m "chore: bump version to $Version"
git tag "v$Version"
git push origin "v$Version"
