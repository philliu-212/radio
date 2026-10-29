param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler is required for the lightweight launcher.' }
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$icon = Join-Path $root 'src\Radio.Windows\Assets\RadioMini.ico'
$store = Join-Path $root 'src\Radio.Update\PortableStore.cs'
$program = Join-Path $root 'src\Radio.Launcher\Program.cs'
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /nowarn:1691 /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Core.dll "/win32icon:$icon" "/out:$OutputPath" $store $program
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
