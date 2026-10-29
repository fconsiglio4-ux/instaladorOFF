param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Path $build, $OutputDirectory -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# Microsoft Download Center, produto 49117, publicado em 19/10/2026.
$url = 'https://download.microsoft.com/download/6c1eeb25-cf8b-41d9-8d0d-cc1dbc032140/officedeploymenttool_20430-20146.exe'
$package = Join-Path $build 'odt.exe'
Invoke-WebRequest -Uri $url -OutFile $package -UseBasicParsing -TimeoutSec 180
function Assert-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw "Assinatura Microsoft invalida: $Path"
    }
}
Assert-MicrosoftSignature $package
$odt = Join-Path $build 'odt'
New-Item -ItemType Directory -Path $odt -Force | Out-Null
$extract = Start-Process -FilePath $package -ArgumentList ('/quiet /extract:"{0}"' -f $odt) -Wait -PassThru
if ($extract.ExitCode -ne 0) { throw "Falha na extracao do ODT: $($extract.ExitCode)" }
$setup = Join-Path $odt 'setup.exe'
Assert-MicrosoftSignature $setup
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$exe = Join-Path $OutputDirectory 'Maquim-Office-Setup.exe'
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll',
    "/win32manifest:$root\src\app.manifest", "/out:$exe", "/resource:$setup,setup.exe",
    "/resource:$root\src\Install-Office.ps1,Install-Office.ps1", "/resource:$root\src\OfficeMedia.psm1,OfficeMedia.psm1", "$root\src\Program.cs")
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao C#.' }
Copy-Item (Join-Path $root 'LEIA-ME.txt') $OutputDirectory -Force
$hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($exe))" | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ASCII
Write-Host "Executavel gerado: $exe"
