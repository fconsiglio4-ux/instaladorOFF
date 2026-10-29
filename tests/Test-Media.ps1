$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
foreach ($script in Get-ChildItem -Path $root -Include *.ps1,*.psm1 -Recurse) {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw ($errors | Out-String) }
}
Import-Module (Join-Path $root 'src\OfficeMedia.psm1') -Force
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Action) {
    $thrown = $false
    try { & $Action | Out-Null } catch { $thrown = $true }
    Assert $thrown 'Era esperado um erro para midia incompleta.'
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('Office & Test-' + [guid]::NewGuid().ToString('N'))
try {
    $data = Join-Path $fixture 'Office\Data'
    $version = Join-Path $data '16.0.20527.20088'
    New-Item -ItemType Directory -Path $version -Force | Out-Null
    foreach ($file in @('v64.cab','16.0.20527.20088\stream.x64.x-none.dat','16.0.20527.20088\stream.x64.pt-br.dat')) {
        New-Item -ItemType File -Path (Join-Path $data $file) | Out-Null
    }
    # Uma versao mais recente incompleta nao deve substituir a midia completa.
    New-Item -ItemType Directory -Path (Join-Path $data '16.0.99999.99999') | Out-Null
    $media = Get-OfficeMedia -Root $fixture -Architecture 64 -Language pt-br
    Assert ($media.Version -eq '16.0.20527.20088') 'A selecao da versao local falhou.'
    [xml]$xml = New-OfficeConfiguration -Media $media
    Assert ($xml.Configuration.Add.SourcePath -eq $fixture) 'Caminho com espacos e & foi corrompido.'
    Assert ($xml.Configuration.Add.Version -eq $media.Version) 'Versao nao fixada na midia.'
    Assert ($xml.Configuration.Add.AllowCdnFallback -eq 'False') 'Fallback CDN habilitado.'
    Assert ($xml.Configuration.Add.Product.ID -eq 'O365ProPlusRetail') 'Produto incorreto.'
    Assert ($xml.Configuration.Add.Product.Language.ID -eq 'pt-br') 'Idioma incorreto.'
    Assert ($null -eq $xml.SelectSingleNode('//Remove')) 'Remocao inesperada de Office.'
    Assert ($xml.SelectSingleNode('//Property[@Name="FORCEAPPSHUTDOWN"]').GetAttribute('Value') -eq 'FALSE') 'Fechamento forcado habilitado.'
    Assert-Throws { Get-OfficeMedia -Root $fixture -Architecture 32 -Language pt-br }
    Assert-Throws { Get-OfficeMedia -Root $fixture -Architecture 64 -Language en-us }
    Remove-Item -LiteralPath (Join-Path $version 'stream.x64.x-none.dat')
    Assert-Throws { Get-OfficeMedia -Root $fixture -Architecture 64 -Language pt-br }
    Write-Host 'OK: sintaxe, produto, idioma, arquitetura, versao local, caminhos e midia incompleta.'
}
finally { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
