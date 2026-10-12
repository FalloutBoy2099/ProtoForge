<#
.SYNOPSIS
  Starts the built ProtoForge on a game and checks, through UI Automation, that the window opens and the prototype list is filled.
.DESCRIPTION
  The game is only read: the output folder is a temporary one. Exit code 0 when everything is fine.
.EXAMPLE
  ./Tools/Smoke.ps1 "C:\Fallout2\F2_RPU_2.4.34"
#>
param(
	[Parameter(Mandatory = $true)][string]$GameFolder,
	[string]$Executable = "",
	[int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Executable) {
	$Executable = Join-Path (Split-Path -Parent $root) "ProtoForge.UI/bin/Debug/net10.0-windows/ProtoForge.exe"
}
if (-not (Test-Path $Executable)) { throw "Executable not found: $Executable (build the solution first)." }
if (-not (Test-Path $GameFolder)) { throw "Game folder not found: $GameFolder" }

# Opening a project adds it to the recent list of the user: the settings are put back afterwards.
$settingsFile = Join-Path $env:APPDATA "ProtoForge/settings.json"
$settingsBackup = if (Test-Path $settingsFile) { [System.IO.File]::ReadAllBytes($settingsFile) } else { $null }

$output = Join-Path ([System.IO.Path]::GetTempPath()) ("protoforge-smoke-" + [guid]::NewGuid().ToString("N"))
$process = Start-Process $Executable -ArgumentList "`"$GameFolder`"", "--output", "`"$output`"" -PassThru
$failures = @()
try {
	$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
	$window = $null
	while ((Get-Date) -lt $deadline -and -not $window) {
		Start-Sleep -Milliseconds 500
		$process.Refresh()
		if ($process.HasExited) { throw "The application stopped at start-up (exit code $($process.ExitCode))." }
		if ($process.MainWindowHandle -ne 0) { $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle) }
	}
	if (-not $window) { throw "No main window after $TimeoutSeconds seconds." }

	# the list is filled when data rows exist
	$rowCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
	$rows = 0
	while ((Get-Date) -lt $deadline -and $rows -eq 0) {
		Start-Sleep -Milliseconds 500
		$rows = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rowCondition).Count
	}
	if ($rows -eq 0) { $failures += "The prototype list stayed empty." } else { Write-Host "List filled ($rows visible rows)." }

	# the search box and the title bar buttons exist
	foreach ($id in "Input", "ThemeButton", "LanguageButton", "OpenButton", "HistoryButton") {
		$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
		if (-not $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)) { $failures += "Missing element: $id" }
	}

	if (Test-Path $output) { $failures += "The application wrote into the output folder without a save: $output" }
}
finally {
	if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; if (-not $process.WaitForExit(5000)) { $process.Kill() } }
	if (Test-Path $output) { Remove-Item $output -Recurse -Force }
	if ($null -ne $settingsBackup) { [System.IO.File]::WriteAllBytes($settingsFile, $settingsBackup) }
}

if ($failures.Count -gt 0) { $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }; exit 1 }
Write-Host "Smoke test passed."
