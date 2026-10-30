param([Parameter(Mandatory=$true)][string]$OutputPath,
      [Parameter(Mandatory=$true)][string]$AssemblyPath,
      [Parameter(Mandatory=$true)][string]$RelativeDll)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$hostPackage = Get-ChildItem (Join-Path $cache 'microsoft.netcore.app.host.win-x64') -Directory |
    Where-Object { $_.Name -match '^8\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$hostPackage) { throw 'Publish win-x64 first so that the .NET 8 apphost template is available.' }
$template = Join-Path $hostPackage.FullName 'runtimes/win-x64/native/apphost.exe'
& dotnet run --project (Join-Path $root 'tools/Radio.AppHost.Build') -c Release -- $template ([IO.Path]::GetFullPath($OutputPath)) $RelativeDll ([IO.Path]::GetFullPath($AssemblyPath))
if ($LASTEXITCODE -ne 0) { throw 'Native apphost generation failed.' }
