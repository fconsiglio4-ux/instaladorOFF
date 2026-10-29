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
    & $harness
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
    $first = [IO.File]::ReadAllText($files[0].FullName)
    $report = $first | ConvertFrom-Json
    if ($report.computer_name -ne $env:COMPUTERNAME -or $report.asset_tag -ne 'CI-000123' -or $report.disks.Count -lt 1) { throw 'Coleta real do servico incorreta.' }
    $details = Get-CimInstance Win32_Service -Filter "Name='$name'"
    if ($details.StartMode -ne 'Auto' -or -not (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$name").DelayedAutoStart) { throw 'Inicio automatico atrasado ausente.' }
    if ($details.StartName -ne 'NT AUTHORITY\LocalService') { throw 'Conta do servico incorreta.' }
    Invoke-SC @('stop', $name)
    (Get-Service $name).WaitForStatus('Stopped', [timespan]::FromSeconds(45))
    Invoke-SC @('start', $name)
    (Get-Service $name).WaitForStatus('Running', [timespan]::FromSeconds(30))
    if ([IO.File]::ReadAllText($files[0].FullName) -ne $first) { throw 'Reinicio alterou o registro pendente.' }
    if (Test-Path (Join-Path $root 'token.dpapi')) { throw 'Teste nao pode criar token.' }
    Write-Host 'Servico real: LocalService, coleta, fila offline e reinicio validados sem token e sem envio em producao.'
} finally {
    $svc = Get-Service $name -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -ne 'Stopped') { Stop-Service $name; $svc.WaitForStatus('Stopped', [timespan]::FromSeconds(45)) }
        & sc.exe delete $name | Out-Null
    }
    if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    if (Test-Path $binaryDirectory) { Remove-Item -LiteralPath $binaryDirectory -Recurse -Force }
}
