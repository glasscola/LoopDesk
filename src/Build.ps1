param([string]$FileName = 'LoopDesk.exe')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
$output = Join-Path $PSScriptRoot '..\outputs'
New-Item -Path $output -ItemType Directory -Force | Out-Null
$refs = @('System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Runtime.WindowsRuntime.dll','Microsoft.VisualBasic.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','UIAutomationClient.dll','UIAutomationTypes.dll','System.Xaml.dll')
$options = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',
    "/out:$output\$FileName",
    "/win32manifest:$PSScriptRoot\LoopDesk.manifest",
    "/resource:$PSScriptRoot\Desk.xaml,Desk.xaml",
    "/lib:$framework,$framework\WPF",
    "/reference:System.Runtime.dll",
    "/reference:$metadata\Windows.Media.winmd",
    "/reference:$metadata\Windows.Foundation.winmd",
    "/reference:$metadata\Windows.Graphics.winmd",
    "/reference:$metadata\Windows.Storage.winmd",
    "/reference:$metadata\Windows.Globalization.winmd")
$options += $refs | ForEach-Object { "/reference:$_" }
$options += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
& "$framework\csc.exe" @options
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
