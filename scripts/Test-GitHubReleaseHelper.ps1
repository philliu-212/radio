$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $root ('artifacts/release-helper-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$testRoot/src/Radio.Windows","$testRoot/dist","$testRoot/tools" -Force | Out-Null
$oldPath = $env:PATH
$oldScenario = $env:RADIO_RELEASE_TEST_SCENARIO
$oldLog = $env:RADIO_RELEASE_TEST_LOG
try {
    # A real native process is required to reproduce PowerShell 5 stderr behavior.
    $source = @'
using System;
using System.IO;
class FakeGh {
    static int Main(string[] args) {
        File.AppendAllText(Environment.GetEnvironmentVariable("RADIO_RELEASE_TEST_LOG"), String.Join(" ", args) + "\n");
        if (args[1] == "create") return 0;
        string scenario = Environment.GetEnvironmentVariable("RADIO_RELEASE_TEST_SCENARIO");
        if (scenario == "existing") return 0;
        Console.Error.WriteLine(scenario == "missing" ? "release not found" :
            scenario == "auth" ? "HTTP 401: Bad credentials" :
            scenario == "not-found" ? "HTTP 404: Not Found" : "error connecting to api.github.com");
        return scenario == "unexpected-exit" ? 2 : 1;
    }
}
'@
    $sourcePath = Join-Path $testRoot 'FakeGh.cs'
    [IO.File]::WriteAllText($sourcePath, $source)
    & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe "/out:$testRoot/tools/gh.exe" $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
    [IO.File]::WriteAllText("$testRoot/src/Radio.Windows/Radio.Windows.csproj", '<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>')
    foreach ($name in @('Radio-Portable-v1.0.0-Lite-win-x64-test.zip','Radio-Portable-v1.0.0-Full-win-x64-test.zip','SHA256SUMS-v1.0.0.txt')) {
        [IO.File]::WriteAllText((Join-Path "$testRoot/dist" $name), 'fixture')
    }
    & git -C $testRoot init --quiet
    & git -C $testRoot -c user.name=Test -c user.email=test@example.invalid commit --allow-empty -m fixture --quiet
    & git -C $testRoot tag v1.0.0
    if ($LASTEXITCODE -ne 0) { throw 'Fixture repository setup failed.' }
    $env:PATH = "$testRoot/tools;$oldPath"
    foreach ($scenario in @('missing','existing','auth','network','not-found','unexpected-exit')) {
        $env:RADIO_RELEASE_TEST_SCENARIO = $scenario
        $env:RADIO_RELEASE_TEST_LOG = Join-Path $testRoot "$scenario.log"
        try {
            $ErrorActionPreference = 'Continue'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/Publish-GitHubRelease.ps1" -Tag v1.0.0 -ProjectRoot $testRoot *> "$testRoot/$scenario-output.txt"
            $exit = $LASTEXITCODE
        } finally { $ErrorActionPreference = 'Stop' }
        $calls = @(Get-Content -LiteralPath $env:RADIO_RELEASE_TEST_LOG)
        if ($scenario -eq 'missing') {
            if ($exit -ne 0 -or $calls.Count -ne 2 -or $calls[1] -notlike 'release create v1.0.0 --verify-tag --draft*' -or
                ([regex]::Matches($calls[1], '\.zip')).Count -ne 2 -or $calls[1] -notlike '*SHA256SUMS-v1.0.0.txt*') {
                throw "Missing release did not create exactly the expected draft: $testRoot"
            }
        } elseif ($exit -eq 0 -or $calls.Count -ne 1) { throw "Unsafe release creation for $scenario" }
        Write-Output "PASS: $scenario"
    }
    Write-Output "Windows PowerShell $($PSVersionTable.PSVersion) native-process regression passed. No GitHub mutations."
} finally {
    $env:PATH = $oldPath
    $env:RADIO_RELEASE_TEST_SCENARIO = $oldScenario
    $env:RADIO_RELEASE_TEST_LOG = $oldLog
}
