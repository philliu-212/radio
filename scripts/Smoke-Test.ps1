param(
    [switch]$Playback,
    [switch]$ExpectLocalRuntime,
    [string]$ExecutablePath,
    [ValidateSet('system', 'native', 'mediaengine')][string]$Backend = 'system'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class RadioWindowTest {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
}
'@

$exe = if ($ExecutablePath) { $ExecutablePath } else {
    Join-Path $PSScriptRoot '..\src\Radio.Windows\bin\Debug\net8.0-windows10.0.19041.0\Radio.exe'
}
if (!(Test-Path $exe)) { throw 'Run Run.cmd first, or specify -ExecutablePath for a release package.' }
$exe = (Resolve-Path -LiteralPath $exe).Path
$process = Start-Process -FilePath $exe -ArgumentList '--backend', $Backend -WorkingDirectory (Split-Path -Parent $exe) -PassThru

function Wait-Until([scriptblock]$Condition, [string]$Message, [int]$Seconds = 10) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw $Message
}

function Find-Control($Root, $Type, [string]$Name = '') {
    $conditions = [Collections.Generic.List[System.Windows.Automation.Condition]]::new()
    $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $Type))
    if ($Name) {
        $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Name))
    }
    $condition = if ($conditions.Count -eq 1) { $conditions[0] } else {
        [System.Windows.Automation.AndCondition]::new($conditions.ToArray())
    }
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

try {
    Wait-Until { $process.Refresh(); $process.MainWindowHandle -ne [IntPtr]::Zero } 'Window did not open.'
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    if ($ExpectLocalRuntime) {
        $runtime = @($process.Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' })[0]
        $directory = (Split-Path -Parent $exe).TrimEnd('\') + '\'
        if ($null -eq $runtime -or !$runtime.FileName.StartsWith($directory, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The Full package did not load its own bundled runtime.'
        }
        Write-Output 'PASS: Full package uses its own local coreclr.dll.'
    }
    $type = [System.Windows.Automation.ControlType]
    $list = Find-Control $window $type::List
    if ($null -eq $list) { throw 'Station list not found.' }
    $items = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type::ListItem))
    if ($items.Count -ne 6) { throw "Expected six stations, found $($items.Count)." }
    Write-Output 'PASS: Native window contains six stations.'

    # Always mute before a playback test; never change the system volume.
    $mute = Find-Control $window $type::CheckBox
    $toggle = $mute.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
    Write-Output 'PASS: Mute control works.'

    $playButton = Find-Control $window $type::Button (([string][char]0x64AD) + [char]0x653E)
    if ($null -eq $playButton) { throw 'Expected idle play button.' }
    if ($Playback) {
        $playButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Wait-Until {
            $texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type::Text))
            @($texts | Where-Object { $_.Current.Name -match '\u64ad\u653e\u4e2d' }).Count -gt 0
        } 'Muted UI playback did not start.' 40
        Write-Output 'PASS: Muted playback starts from the UI.'
        $stopButton = Find-Control $window $type::Button (([string][char]0x505C) + [char]0x6B62)
        if ($null -eq $stopButton) { throw 'Playing did not switch the transport button to Stop.' }
        $stopButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Write-Output 'PASS: Live transport switches to Stop and can disconnect.'
    }

    $minimize = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MinimizeButton'))
    $minimize.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-Until { ![RadioWindowTest]::IsWindowVisible($process.MainWindowHandle) } 'Minimize did not hide to tray.'
    Write-Output 'PASS: Minimize hides the window to tray.'
    [RadioWindowTest]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
    Wait-Until { [RadioWindowTest]::IsWindowVisible($process.MainWindowHandle) } 'Window could not be restored.'
    $close = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'CloseButton'))
    $close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if (!$process.WaitForExit(10000)) { throw 'Window close did not exit.' }
    if ($process.ExitCode -ne 0) { throw "Application exited with $($process.ExitCode)." }
    Write-Output 'PASS: Window closes cleanly.'
}
finally {
    if (!$process.HasExited) {
        # Only terminate the instance created by this test, never another user instance.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
        $process.WaitForExit()
    }
    $process.Dispose()
}
