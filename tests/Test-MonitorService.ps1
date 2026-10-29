param([Parameter(Mandatory=$true)][string]$ServiceExecutable)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Homologacao somente em runner/VM descartavel; nenhum token, nenhum POST em producao.
$name = 'MaquimComputerMonitor'
$root = Join-Path $env:ProgramData 'Maquim\ComputerMonitor'
if (Test-Path $root) { throw 'Teste exige maquina limpa: nao sobrescreve configuracao existente.' }
if (Get-Service -Name $name -ErrorAction SilentlyContinue) { throw 'Servico existente; teste cancelado.' }
$binaryDirectory = Join-Path $env:ProgramFiles 'Maquim\ComputerMonitor'
if (Test-Path $binaryDirectory) { throw 'Teste nao sobrescreve binarios existentes.' }
function Invoke-SC([string[]]$Arguments) {
    & sc.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "SC falhou: $($Arguments[0]) ($LASTEXITCODE)" }
}
try {
    $harness = Join-Path (Split-Path (Resolve-Path $ServiceExecutable).Path -Parent) 'MonitorProvision.exe'
    $legacyHarness = Join-Path (Split-Path $harness -Parent) 'MonitorProvisionLegacy.exe'
    & $legacyHarness
    if ($LASTEXITCODE -ne 0) { throw 'Provisionamento real do servico falhou.' }
    # Nao ha token.dpapi. O servico deve persistir offline sem tentar HTTP.
    (Get-Service $name).WaitForStatus('Running', [timespan]::FromSeconds(30))
    $limit = [datetime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path (Join-Path $root 'state.json')) -and [datetime]::UtcNow -lt $limit) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path (Join-Path $root 'state.json'))) {
        $status = Join-Path $root 'status.txt'
        if (Test-Path $status) { Get-Content $status | Write-Host }
        throw 'Servico nao gravou status como LocalService.'
    }
    $files = @(Get-ChildItem (Join-Path $root 'queue') -Filter '*.json')
    if ($files.Count -ne 1) { throw 'Esperado um registro offline.' }
    $oldHash = (Get-FileHash (Join-Path $binaryDirectory 'Maquim-ComputerMonitor.exe') -Algorithm SHA256).Hash
    $first = [IO.File]::ReadAllText($files[0].FullName)
    $report = $first | ConvertFrom-Json
    if ($report.computer_name -ne $env:COMPUTERNAME -or $report.asset_tag -ne 'CI-000123' -or $report.disks.Count -lt 1) { throw 'Coleta real do servico incorreta.' }
    if ($report.agent_version -ne '1.3.0') { throw 'Fixture anterior incorreta.' }
    $details = Get-CimInstance Win32_Service -Filter "Name='$name'"
    if ($details.StartMode -ne 'Auto' -or -not (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$name").DelayedAutoStart) { throw 'Inicio automatico atrasado ausente.' }
    if ($details.StartName -ne 'NT AUTHORITY\LocalService') { throw 'Conta do servico incorreta.' }
    Invoke-SC @('stop', $name)
    (Get-Service $name).WaitForStatus('Stopped', [timespan]::FromSeconds(45))
    Invoke-SC @('start', $name)
    (Get-Service $name).WaitForStatus('Running', [timespan]::FromSeconds(30))
    if ([IO.File]::ReadAllText($files[0].FullName) -ne $first) { throw 'Reinicio alterou o registro pendente.' }
    if (Test-Path (Join-Path $root 'token.dpapi')) { throw 'Teste nao pode criar token.' }
    $startup = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run').MaquimComputerLocation
    if ($startup -notmatch 'Maquim-ComputerLocation-1.4.0.exe') { throw 'Auxiliar nao configurado para o login.' }
    if (Test-Path (Join-Path $root 'ip-location.json')) { throw 'Teste sem token nao deve consultar IP publico.' }
    Invoke-SC @('stop', $name)
    (Get-Service $name).WaitForStatus('Stopped', [timespan]::FromSeconds(45))
    $configPath = Join-Path $root 'config.json'
    $configuration = [IO.File]::ReadAllBytes($configPath)
    $beforeConfig = [IO.File]::ReadAllText($configPath) | ConvertFrom-Json
    Add-Type -AssemblyName System.Security
    $emptyToken = [Security.Cryptography.ProtectedData]::Protect([byte[]]@(), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes((Join-Path $root 'token.dpapi'), $emptyToken)
    # Uma fila bloqueada e caches devem permanecer byte a byte.
    $blocked = $files[0].FullName + '.blocked'
    [IO.File]::WriteAllText($blocked, 'CI: formato bloqueado para teste de preservacao')
    [IO.File]::WriteAllText((Join-Path $root 'auth-paused'), '401')
    & $harness --wrong-identity
    if ($LASTEXITCODE -ne 0) { throw 'Teste de identidade alterada falhou.' }
    & $harness --update
    if ($LASTEXITCODE -ne 0) { throw 'Atualizacao real do agente falhou.' }
    (Get-Service $name).WaitForStatus('Running', [timespan]::FromSeconds(30))
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($configPath)) -ne [Convert]::ToBase64String($configuration)) { throw 'Atualizacao alterou IDs, serie, patrimonio ou vinculo.' }
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $root 'token.dpapi'))) -ne [Convert]::ToBase64String($emptyToken)) { throw 'Atualizacao alterou token salvo.' }
    if ([IO.File]::ReadAllText($files[0].FullName) -ne $first -or -not (Test-Path $blocked) -or -not (Test-Path (Join-Path $root 'auth-paused'))) { throw 'Atualizacao perdeu fila ou bloqueios.' }
    if (Test-Path (Join-Path $root 'ip-location.json')) { throw 'Atualizacao sem token consultou localizacao externa.' }
    $newHash = (Get-FileHash (Join-Path $binaryDirectory 'Maquim-ComputerMonitor.exe') -Algorithm SHA256).Hash
    $packageHash = (Get-FileHash $ServiceExecutable -Algorithm SHA256).Hash
    if ($oldHash -eq $newHash -or $newHash -ne $packageHash) { throw 'Atualizacao nao substituiu binario antigo pelo pacote novo.' }
    $limit = [datetime]::UtcNow.AddSeconds(30)
    do {
        $state = Get-Content (Join-Path $root 'state.json') -Raw | ConvertFrom-Json
        if ($state.agent_version -eq '1.4.0') { break }
        Start-Sleep -Milliseconds 250
    } while ([datetime]::UtcNow -lt $limit)
    if ($state.agent_version -ne '1.4.0' -or $state.sequence_number -le $report.sequence_number) { throw 'Atualizacao nao retomou coleta com sequencia crescente.' }
    Write-Host 'Instalacao independente e atualizacao reais: sem Office; configuracao, IDs, token DPAPI, filas e bloqueios preservados.'
    Write-Host 'Servico real: LocalService, coleta, fila offline e reinicio validados sem token e sem envio em producao.'
} finally {
    $svc = Get-Service $name -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -ne 'Stopped') { Stop-Service $name; $svc.WaitForStatus('Stopped', [timespan]::FromSeconds(45)) }
        & sc.exe delete $name | Out-Null
    }
    if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    if (Test-Path $binaryDirectory) { Remove-Item -LiteralPath $binaryDirectory -Recurse -Force }
    Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'MaquimComputerLocation' -ErrorAction SilentlyContinue
}
