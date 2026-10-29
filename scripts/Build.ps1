param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'), [switch]$TestOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'build'
if ($TestOnly) { $OutputDirectory = Join-Path $build 'test-output' }
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
$commonReferences = @('/reference:System.Management.dll', '/reference:System.Security.dll', '/reference:System.Web.Extensions.dll', '/reference:System.DirectoryServices.dll', '/reference:System.DirectoryServices.AccountManagement.dll')
$syncExe = Join-Path $build 'Maquim-ComputerSync.exe'
$token = if ($TestOnly) { 'UNIT-TEST-INGEST-TOKEN-NOT-FOR-PRODUCTION' } else { $env:INSTALLATIONS_INGEST_TOKEN }
if ([string]::IsNullOrWhiteSpace($token)) { throw 'Cadastre o segredo INSTALLATIONS_INGEST_TOKEN para gerar o executavel final.' }
$tokenFile = Join-Path $build 'ingest-token.bin'
[IO.File]::WriteAllBytes($tokenFile, [Text.Encoding]::UTF8.GetBytes($token.Trim()))
$token = $null
$password = if ($TestOnly) { 'UNIT-TEST-NOT-A-DEPLOYMENT-PASSWORD' } else { $env:MAQUIM_LOCAL_ADMIN_PASSWORD }
if ([string]::IsNullOrEmpty($password)) { Remove-Item -LiteralPath $tokenFile -Force; throw 'Cadastre o segredo MAQUIM_LOCAL_ADMIN_PASSWORD para gerar o executavel final.' }
$passwordFile = Join-Path $build 'local-admin-password.bin'
[IO.File]::WriteAllBytes($passwordFile, [Text.Encoding]::UTF8.GetBytes($password))
$password = $null
$passwordResource = "/resource:$passwordFile,Maquim.LocalAdminPassword"
$tokenResource = "/resource:$tokenFile,Maquim.IngestToken"
try {
& $compiler '/nologo' '/target:winexe' '/platform:anycpu' '/optimize+' '/codepage:65001' $tokenResource @commonReferences "/win32manifest:$root\src\sync.manifest" "/out:$syncExe" "$root\src\ComputerIntegration.cs" "$root\src\ComputerSyncProgram.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do agente de reenvio.' }
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll',
    "/win32manifest:$root\src\app.manifest", "/out:$exe", "/resource:$setup,setup.exe",
    "/resource:$root\src\Install-Office.ps1,Install-Office.ps1", "/resource:$root\src\OfficeMedia.psm1,OfficeMedia.psm1",
    "/resource:$syncExe,Maquim-ComputerSync.exe", "/resource:$root\src\Register-ComputerSync.ps1,Register-ComputerSync.ps1",
    "$root\src\Program.cs", "$root\src\ComputerIntegration.cs", "$root\src\SyncAgent.cs", "$root\src\LocalAdminAccount.cs", $tokenResource, $passwordResource) + $commonReferences
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao C#.' }
# Testes usam o mesmo codigo da interface e integracao, sempre com transporte simulado.
$tests = Join-Path $build 'IntegrationTests.exe'
& $compiler '/nologo' '/target:exe' '/platform:anycpu' '/codepage:65001' '/main:IntegrationTests' '/reference:System.Windows.Forms.dll' '/reference:System.Drawing.dll' $tokenResource @commonReferences "/out:$tests" "$root\src\Program.cs" "$root\src\ComputerIntegration.cs" "$root\src\SyncAgent.cs" "$root\src\LocalAdminAccount.cs" "$root\tests\IntegrationTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao dos testes.' }
& $tests
if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes da integracao.' }
if ($TestOnly) {
    Remove-Item -LiteralPath $exe -Force
    Write-Host 'Compilacao de teste validada e removida; nenhum EXE com token ficticio sera distribuido.'
    return
}
Copy-Item (Join-Path $root 'LEIA-ME.txt') $OutputDirectory -Force
$hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($exe))" | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ASCII
Write-Host "Executavel gerado: $exe"

} finally { Remove-Item -LiteralPath $tokenFile, $passwordFile -Force -ErrorAction SilentlyContinue }
