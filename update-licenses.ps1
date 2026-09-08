param()

$ErrorActionPreference = 'Stop'

$tool = 'thirdlicense'
  $cmd = Get-Command $tool -ErrorAction SilentlyContinue
  $toolPath = if ($cmd) { $cmd.Source } else { $null }

if (-not $toolPath) {
  Write-Host 'Installing thirdlicense tool...'
  dotnet tool install -g thirdlicense
  $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'User')
$cmd = Get-Command $tool -ErrorAction SilentlyContinue
$toolPath = if ($cmd) { $cmd.Source } else { $null }
  if (-not $toolPath) {
    throw "thirdlicense tool not found after install"
  }
}

Write-Host 'Restoring packages...'
dotnet restore VmaCS.slnx

Write-Host 'Generating THIRD-PARTY-NOTICES.txt...'
& $toolPath --project src/VmaCS/VmaCS.csproj --output THIRD-PARTY-NOTICES.txt

Write-Host 'Done.'
