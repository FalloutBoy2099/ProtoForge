<#
.SYNOPSIS
	Builds ProtoForge as two standalone executables in the "publish" folder.

.DESCRIPTION
	ProtoForge.exe      The program alone (framework-dependent, a few MB). It needs the .NET 10 Desktop Runtime on the computer.
	ProtoForge-NET.exe  The program with the .NET runtime inside (about 60 MB). Nothing has to be installed.

	Each file is a single executable. The previous "publish" folder is replaced only when both builds succeeded.

.EXAMPLE
	.\Publish.ps1
	.\Publish.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
	[ValidateSet('Release', 'Debug')]
	[string]$Configuration = 'Release',

	[ValidateSet('win-x64', 'win-x86', 'win-arm64')]
	[string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'ProtoForge.UI\ProtoForge.UI.csproj'
$output = Join-Path $root 'publish'
$staging = Join-Path $root 'publish.tmp'

# Both builds produce "ProtoForge.exe" (the assembly name); the second one is renamed when it is moved to "publish".
$variants = @(
	@{ Name = 'ProtoForge.exe'; SelfContained = $false },
	@{ Name = 'ProtoForge-NET.exe'; SelfContained = $true }
)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
	throw 'The .NET SDK was not found. Install the .NET 10 SDK.'
}

function Remove-Folder([string]$path) {
	if (Test-Path $path) {
		Remove-Item $path -Recurse -Force
	}
}

# The builds go to a temporary folder: the previous publish is replaced only when the new ones succeeded.
Remove-Folder $staging

try {
	foreach ($variant in $variants) {
		$selfContained = if ($variant.SelfContained) { 'true' } else { 'false' }
		$folder = Join-Path $staging ([System.IO.Path]::GetFileNameWithoutExtension($variant.Name))

		$arguments = @(
			'publish', $project,
			'--configuration', $Configuration,
			'--runtime', $Runtime,
			'--self-contained', $selfContained,
			'--output', $folder,
			'-p:PublishSingleFile=true',
			'-p:DebugType=None',
			'-p:DebugSymbols=false'
		)

		if ($variant.SelfContained) {
			# The WPF native libraries are packed into the file and unpacked on first start.
			$arguments += '-p:IncludeNativeLibrariesForSelfExtract=true'
			$arguments += '-p:EnableCompressionInSingleFile=true'
		}

		Write-Host "Publishing $($variant.Name) ($Configuration, $Runtime, self-contained: $selfContained)..." -ForegroundColor Cyan
		& dotnet @arguments
		if ($LASTEXITCODE -ne 0) {
			throw "dotnet publish failed with exit code $LASTEXITCODE for $($variant.Name)."
		}
	}

	# Start from a clean folder so that no file of an older publish is left behind.
	Remove-Folder $output
	New-Item -ItemType Directory -Path $output | Out-Null

	foreach ($variant in $variants) {
		$folder = Join-Path $staging ([System.IO.Path]::GetFileNameWithoutExtension($variant.Name))
		Move-Item (Join-Path $folder 'ProtoForge.exe') (Join-Path $output $variant.Name)
	}
}
finally {
	Remove-Folder $staging
}

foreach ($variant in $variants) {
	$exe = Get-Item (Join-Path $output $variant.Name)
	$size = [math]::Round($exe.Length / 1MB, 1)
	Write-Host "Done: $($exe.FullName) ($size MB)" -ForegroundColor Green
}
