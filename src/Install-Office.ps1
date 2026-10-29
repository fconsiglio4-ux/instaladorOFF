param(
    [Parameter(Mandatory)][string]$MediaPath,
    [Parameter(Mandatory)][string]$WorkDirectory,
    [ValidateSet('32','64')][string]$Architecture = '64',
    [ValidateSet('pt-br','en-us','es-es')][string]$Language = 'pt-br',
    [switch]$Activate,
    [Guid]$InstallationId = ([Guid]::NewGuid())
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'OfficeMedia.psm1') -Force
$mountedHere = $false
$image = $null
$exitCode = 1
$log = Join-Path $WorkDirectory 'installation.log'

function Write-Stage([string]$Message) {
    $line = ('[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $Message)
    Add-Content -LiteralPath $log -Value $line -Encoding UTF8
    Write-Output $line
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Execute o instalador como administrador.'
    }
    if (-not [Environment]::Is64BitOperatingSystem -and $Architecture -eq '64') { throw 'Windows de 32 bits exige Office de 32 bits.' }
    $mediaItem = Get-Item -LiteralPath $MediaPath -ErrorAction Stop
    $openApps = @(Get-Process -Name WINWORD,EXCEL,POWERPNT,OUTLOOK,MSACCESS,ONENOTE,MSPUB,VISIO,WINPROJ -ErrorAction SilentlyContinue)
    if ($openApps.Count -gt 0) { throw 'Feche os aplicativos do Office antes de continuar. Nenhum documento sera fechado automaticamente.' }
    $expectedPlatform = if ($Architecture -eq '64') { 'x64' } else { 'x86' }
    $installed = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction SilentlyContinue
    $installedState = ConvertTo-OfficeInstallationState -Configuration $installed
    if ($installed) {
        if ($installedState.Platform -in @('x64','x86')) {
            if ($installedState.Platform -ne $expectedPlatform) { throw 'A arquitetura do Office existente e diferente. Selecione a mesma arquitetura ou remova a instalacao anterior manualmente.' }
            Write-Stage "Office existente detectado: $($installedState.Platform); produtos: $($installedState.ProductReleaseIds); versao: $($installedState.VersionToReport)."
        }
        else {
            Write-Stage 'Registro anterior do Office sem Platform valido. Continuando para a verificacao do instalador Microsoft.'
        }
    }
    $setup = Join-Path $PSScriptRoot 'setup.exe'
    $signature = Get-AuthenticodeSignature -LiteralPath $setup
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw 'A assinatura Microsoft do Office Deployment Tool nao foi validada.'
    }
    Write-Stage 'Preparando a midia local...'
    if ($mediaItem.PSIsContainer) { $root = $mediaItem.FullName }
    else {
        if ($mediaItem.Extension -notin @('.img','.iso')) { throw 'Selecione um arquivo IMG/ISO ou uma pasta extraida.' }
        $image = Get-DiskImage -ImagePath $mediaItem.FullName -ErrorAction SilentlyContinue
        if (-not $image -or -not $image.Attached) {
            $image = Mount-DiskImage -ImagePath $mediaItem.FullName -StorageType ISO -Access ReadOnly -PassThru
            $mountedHere = $true
        }
        $volumes = @($image | Get-Volume | Where-Object DriveLetter)
        if ($volumes.Count -ne 1) { throw 'Nao foi possivel identificar a unidade da imagem. Extraia o IMG para uma pasta e selecione essa pasta.' }
        $root = '{0}:\' -f $volumes[0].DriveLetter
    }
    $media = Get-OfficeMedia -Root $root -Architecture $Architecture -Language $Language
    Write-Stage "Midia validada: O365ProPlusRetail $Architecture bits / $Language / $($media.Version)."
    $config = Join-Path $WorkDirectory 'configuration.xml'
    New-OfficeConfiguration -Media $media | Set-Content -LiteralPath $config -Encoding UTF8
    Write-Stage 'Instalando Office a partir da midia. Aguarde; esta etapa pode levar varios minutos.'
    $process = Start-Process -FilePath $setup -ArgumentList @('/configure', ('"{0}"' -f $config)) -PassThru -Wait
    if ($process.ExitCode -notin @(0,3010)) { throw "A instalacao retornou o codigo $($process.ExitCode). Consulte os logs do Office na pasta TEMP." }
    $reboot = $process.ExitCode -eq 3010
    $after = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction Stop
    $afterState = ConvertTo-OfficeInstallationState -Configuration $after
    if ('O365ProPlusRetail' -notin ($afterState.ProductReleaseIds -split ',')) { throw 'O instalador terminou, mas O365ProPlusRetail nao foi encontrado no registro.' }
    if (-not $afterState.Platform) { throw 'O instalador terminou, mas o campo Platform nao foi preenchido no registro. Nao foi possivel confirmar a instalacao.' }
    if ($afterState.Platform -ne $expectedPlatform) { throw 'A arquitetura instalada nao corresponde a selecao.' }
    Write-Stage 'Instalacao do Office concluida e produto identificado no registro.'
    # Marcador exclusivo desta execucao; a interface ainda verifica registro e
    # executavel real antes de criar o JSON. Falha de ativacao nao apaga a instalacao.
    $InstallationId.ToString('D') | Set-Content -LiteralPath (Join-Path $WorkDirectory 'office-installed.id') -Encoding UTF8
    if ($Activate -and -not $reboot) {
        Write-Stage 'Baixando Ohook 3.12 da revisao fixa e verificando SHA-256...'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $url = 'https://raw.githubusercontent.com/massgravel/Microsoft-Activation-Scripts/6401d2f36975dc07bcb6d22ea823a8498a75ec01/MAS/Separate-Files-Version/Activators/Ohook_Activation_AIO.cmd'
        $activation = Join-Path $WorkDirectory 'Ohook.cmd'
        Invoke-WebRequest -Uri $url -OutFile $activation -UseBasicParsing -TimeoutSec 120
        $expectedHash = '9c77ecce70a4c7161589dbbe20d9282a0b5071dba6260abb59ca35a765f475ef'
        if ((Get-FileHash -LiteralPath $activation -Algorithm SHA256).Hash -ne $expectedHash) { throw 'A integridade do script de ativacao nao corresponde a revisao aprovada. A execucao foi interrompida.' }
        Write-Stage 'Executando Ohook. O metodo altera componentes de licenciamento do Office.'
        # Usa o parametro nao interativo documentado no proprio script; nao simula teclas.
        $activationLog = Join-Path $WorkDirectory 'activation.log'
        $activationError = Join-Path $WorkDirectory 'activation-error.log'
        $p = Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList ('/d /c ""{0}" /Ohook"' -f $activation) -Wait -PassThru -RedirectStandardOutput $activationLog -RedirectStandardError $activationError
        $output = Get-Content -LiteralPath $activationLog -Raw
        if ($p.ExitCode -ne 0 -or $output -notmatch 'Office is permanently activated\.') {
            throw 'Office instalado, mas o script nao confirmou a ativacao. Consulte activation.log. A instalacao foi preservada.'
        }
        Write-Stage 'O script Ohook informou sucesso. Isso nao constitui verificacao de uma licenca Microsoft.'
    }
    elseif ($reboot) { Write-Stage 'Reinicie o Windows antes de realizar a ativacao. Nenhuma reinicializacao automatica sera feita.' }
    else { Write-Stage 'Ativacao nao solicitada. Use a conta Microsoft 365 licenciada para ativar o Office.' }
    $exitCode = if ($reboot) { 3010 } else { 0 }
    Write-Stage 'Processo concluido.'
}
catch {
    Write-Stage ('ERRO: ' + $_.Exception.Message)
    $exitCode = 1
}
finally {
    if ($mountedHere -and $image) {
        try { Dismount-DiskImage -ImagePath $mediaItem.FullName -ErrorAction Stop | Out-Null }
        catch { Write-Stage ('Nao foi possivel desmontar a imagem: ' + $_.Exception.Message) }
    }
}
exit $exitCode
