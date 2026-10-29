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
$commonReferences = @('/reference:System.ServiceProcess.dll', '/reference:System.Management.dll', '/reference:System.Security.dll', '/reference:System.Web.Extensions.dll', '/reference:System.DirectoryServices.dll', '/reference:System.DirectoryServices.AccountManagement.dll')
$syncExe = Join-Path $build 'Maquim-ComputerSync.exe'
& $compiler '/nologo' '/target:winexe' '/platform:anycpu' '/optimize+' '/codepage:65001' @commonReferences "/win32manifest:$root\src\sync.manifest" "/out:$syncExe" "$root\src\ComputerIntegration.cs" "$root\src\ComputerSyncProgram.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do agente de reenvio.' }
$monitorExe = Join-Path $build 'Maquim-ComputerMonitor.exe'
& $compiler '/nologo' '/target:winexe' '/platform:anycpu' '/optimize+' '/codepage:65001' @commonReferences "/win32manifest:$root\src\sync.manifest" "/out:$monitorExe" "$root\src\ComputerIntegration.cs" "$root\src\MonitorCore.cs" "$root\src\LocationCore.cs" "$root\src\MonitorServiceProgram.cs" "$root\src\LocationPipe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do servico de monitoramento.' }
$locationExe = Join-Path $build 'Maquim-ComputerLocation.exe'
& $compiler '/nologo' '/target:winexe' '/platform:anycpu' '/optimize+' '/codepage:65001' '/reference:System.Device.dll' '/reference:System.Windows.Forms.dll' '/reference:System.Drawing.dll' @commonReferences "/win32manifest:$root\src\sync.manifest" "/out:$locationExe" "$root\src\ComputerIntegration.cs" "$root\src\LocationCore.cs" "$root\src\LocationPipe.cs" "$root\src\LocationHelperProgram.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do auxiliar de localizacao.' }
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Core.dll',
    "/win32manifest:$root\src\app.manifest", "/out:$exe", "/resource:$setup,setup.exe",
    "/resource:$root\src\Install-Office.ps1,Install-Office.ps1", "/resource:$root\src\OfficeMedia.psm1,OfficeMedia.psm1",
    "/resource:$locationExe,Maquim-ComputerLocation.exe", "/resource:$monitorExe,Maquim-ComputerMonitor.exe", "/resource:$syncExe,Maquim-ComputerSync.exe", "/resource:$root\src\Register-ComputerSync.ps1,Register-ComputerSync.ps1",
    "$root\src\Program.cs", "$root\src\AgentSetupForm.cs", "$root\src\AgentSetupPlan.cs", "$root\src\ComputerIntegration.cs", "$root\src\SyncAgent.cs", "$root\src\MonitorCore.cs", "$root\src\LocationCore.cs", "$root\src\MonitorInstaller.cs", "$root\src\LocalAdminAccount.cs", "$root\src\AccountPasswordDialog.cs") + $commonReferences
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao C#.' }
$agentSetupExe = Join-Path $OutputDirectory 'Maquim-Agente-Setup.exe'
& $compiler '/nologo' '/target:winexe' '/platform:anycpu' '/optimize+' '/codepage:65001' '/reference:System.Windows.Forms.dll' '/reference:System.Drawing.dll' @commonReferences "/win32manifest:$root\src\app.manifest" "/out:$agentSetupExe" "/resource:$locationExe,Maquim-ComputerLocation.exe" "/resource:$monitorExe,Maquim-ComputerMonitor.exe" "$root\src\AgentSetupProgram.cs" "$root\src\AgentSetupForm.cs" "$root\src\AgentSetupPlan.cs" "$root\src\MonitorInstaller.cs" "$root\src\MonitorCore.cs" "$root\src\LocationCore.cs" "$root\src\ComputerIntegration.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do instalador independente do agente.' }
# Verifica os binarios reais sem executa-los ou criar contas no runner.
foreach ($binary in @($exe, $agentSetupExe, $syncExe, $monitorExe, $locationExe)) {
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($binary))
    $resources = $assembly.GetManifestResourceNames()
    if (@($resources | Where-Object { $_ -match 'IngestToken|LocalAdminPassword' }).Count -gt 0) {
        throw 'O binario nao pode conter recursos de credenciais.'
    }
}
$agentAssembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($agentSetupExe))
if (@($agentAssembly.GetManifestResourceNames() | Where-Object { $_ -match 'setup.exe|Install-Office|OfficeMedia|ComputerSync|Register-ComputerSync' }).Count -gt 0) { throw 'Instalador do agente nao pode conter recursos de instalacao do Office.' }
if (@($agentAssembly.GetTypes() | Where-Object { $_.Name -match 'LocalAdminAccount|InstallerForm|AccountPasswordDialog' }).Count -gt 0) { throw 'Instalador do agente nao pode criar contas ou executar instalador do Office.' }
# Testes usam o mesmo codigo da interface e integracao, sempre com transporte simulado.
$tests = Join-Path $build 'IntegrationTests.exe'
& $compiler '/nologo' '/target:exe' '/platform:anycpu' '/codepage:65001' '/main:IntegrationTests' '/reference:System.Windows.Forms.dll' '/reference:System.Drawing.dll' @commonReferences "/out:$tests" "$root\src\Program.cs" "$root\src\AgentSetupForm.cs" "$root\src\AgentSetupPlan.cs" "$root\src\ComputerIntegration.cs" "$root\src\SyncAgent.cs" "$root\src\MonitorCore.cs" "$root\src\LocationCore.cs" "$root\src\MonitorInstaller.cs" "$root\src\LocalAdminAccount.cs" "$root\src\AccountPasswordDialog.cs" "$root\tests\IntegrationTests.cs" "$root\tests\AgentSetupTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao dos testes.' }
& $tests
if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes da integracao.' }
$monitorTests = Join-Path $build 'MonitorTests.exe'
& $compiler '/nologo' '/target:exe' '/platform:anycpu' '/codepage:65001' @commonReferences "/out:$monitorTests" "$root\src\ComputerIntegration.cs" "$root\src\MonitorCore.cs" "$root\src\LocationCore.cs" "$root\src\LocationPipe.cs" "$root\tests\MonitorTests.cs" "$root\tests\LocationTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao dos testes de monitoramento.' }
& $monitorTests
if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes de monitoramento.' }
$provisionTests = Join-Path $build 'MonitorProvision.exe'
& $compiler '/nologo' '/target:exe' '/platform:anycpu' '/codepage:65001' @commonReferences "/out:$provisionTests" "/resource:$monitorExe,Maquim-ComputerMonitor.exe" "/resource:$locationExe,Maquim-ComputerLocation.exe" "$root\src\ComputerIntegration.cs" "$root\src\MonitorCore.cs" "$root\src\LocationCore.cs" "$root\src\MonitorInstaller.cs" "$root\src\AgentSetupPlan.cs" "$root\tests\MonitorProvision.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao da homologacao do servico.' }
# Runner usa um servico com versao anterior para exercitar troca real do binario.
$legacyIntegration = Join-Path $build 'ComputerIntegration-legacy.cs'
([IO.File]::ReadAllText("$root\src\ComputerIntegration.cs").Replace('Version = "1.4.0"', 'Version = "1.3.0"')) | Set-Content -LiteralPath $legacyIntegration -Encoding UTF8
$legacyMonitor = Join-Path $build 'Maquim-ComputerMonitor-legacy.exe'
& $compiler '/nologo' '/target:winexe' '/platform:anycpu' '/codepage:65001' @commonReferences "/out:$legacyMonitor" $legacyIntegration "$root\src\MonitorCore.cs" "$root\src\LocationCore.cs" "$root\src\LocationPipe.cs" "$root\src\MonitorServiceProgram.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao da fixture de versao anterior.' }
$legacyProvision = Join-Path $build 'MonitorProvisionLegacy.exe'
& $compiler '/nologo' '/target:exe' '/platform:anycpu' '/codepage:65001' @commonReferences "/out:$legacyProvision" "/resource:$legacyMonitor,Maquim-ComputerMonitor.exe" "/resource:$locationExe,Maquim-ComputerLocation.exe" "$root\src\ComputerIntegration.cs" "$root\src\MonitorCore.cs" "$root\src\LocationCore.cs" "$root\src\MonitorInstaller.cs" "$root\src\AgentSetupPlan.cs" "$root\tests\MonitorProvision.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilacao do provisionamento de fixture.' }
if ($TestOnly) {
    Remove-Item -LiteralPath $exe, $agentSetupExe -Force
    Write-Host 'Compilacao de teste validada e removida; credenciais sao fornecidas somente em tempo de execucao.'
    return
}
Copy-Item (Join-Path $root 'LEIA-ME.txt') $OutputDirectory -Force
$hashes = foreach ($binary in @($exe, $agentSetupExe)) { $hash = Get-FileHash -LiteralPath $binary -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($binary))" }
$hashes | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ASCII
Write-Host "Executavel gerado: $exe"

