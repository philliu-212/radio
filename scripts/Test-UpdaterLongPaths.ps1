$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root ('artifacts/updater-long-paths-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $output 'LongPathTests.exe'
& $compiler /nologo /target:exe /main:LongPathTests /platform:x64 /optimize+ /nowarn:1691 /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Core.dll "/out:$exe" (Join-Path $root 'src\Radio.Update\PortableStore.cs') (Join-Path $root 'src\Radio.Updater\Program.cs') (Join-Path $root 'tests\Radio.Updater.Tests\LongPathTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Framework regression compilation failed.' }
& $exe legacy $output
if ($LASTEXITCODE -ne 0) { throw 'Legacy negative control failed.' }
& $exe fixed $output
if ($LASTEXITCODE -ne 0) { throw 'Long-path / reparse-point regression failed.' }
Write-Output "Framework updater regression passed: $output"
