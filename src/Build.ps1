param([string]$FileName = '오토매크로v10.exe')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$output = Join-Path $PSScriptRoot '..\outputs'
New-Item -Path $output -ItemType Directory -Force | Out-Null
$refs = @('System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','Microsoft.VisualBasic.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll')
$options = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',
    "/out:$output\$FileName",
    "/win32manifest:$PSScriptRoot\AutoMacro.manifest",
    "/resource:$PSScriptRoot\Desk.xaml,Desk.xaml",
    "/win32icon:$PSScriptRoot\AutoMacro.ico",
    "/resource:$PSScriptRoot\AutoMacro.ico,AutoMacro.ico",
    "/lib:$framework,$framework\WPF",
    "/reference:System.Runtime.dll")
$options += $refs | ForEach-Object { "/reference:$_" }
$options += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
& "$framework\csc.exe" @options
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }

