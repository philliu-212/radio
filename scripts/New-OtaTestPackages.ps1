param([ValidateSet('Lite','Full')][string]$Mode = 'Lite')
# Explicit test builds only. Synthetic identities and versions are never uploaded.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root ('artifacts/ota-test-packages-' + $Mode + '-' + [Guid]::NewGuid().ToString('N'))
$runtime = Get-Content -Raw (Join-Path $PSScriptRoot 'runtime-package.json') | ConvertFrom-Json
foreach ($version in @('1.0.0','1.1.0','1.2.0')) {
    $payload = Join-Path $output ('payload-' + $version)
    $portable = Join-Path $output ('Radio-Portable-v' + $version + '-' + $Mode + '-win-x64-test')
    $arguments = @('publish',(Join-Path $root 'src/Radio.Windows'),'-c','Release','-r','win-x64','--self-contained',($Mode -eq 'Full').ToString().ToLowerInvariant(),
        '-o',$payload,'--artifacts-path',(Join-Path $output ('build-' + $version)),"-p:Version=$version",'-p:DebugType=None','-p:DebugSymbols=false')
    if ($Mode -eq 'Full') { $arguments += '-p:PortableRuntimeVersion=' + $runtime.version }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    $sha = [Security.Cryptography.SHA1]::Create()
    try { $identity = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('RadioMini-OTA-test-' + $version)))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
    & (Join-Path $PSScriptRoot 'New-PortableLayout.ps1') -InputDirectory $payload -OutputDirectory $portable -Version $version -Commit $identity -Mode $Mode
    Compress-Archive -LiteralPath $portable -DestinationPath ($portable + '.zip')
}
Write-Output "TEST ONLY packages: $output"
