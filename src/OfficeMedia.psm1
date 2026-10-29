Set-StrictMode -Version Latest

function Get-OfficeMedia {
    param([Parameter(Mandatory)][string]$Root, [ValidateSet('32','64')][string]$Architecture = '64', [string]$Language = 'pt-br')
    $data = Join-Path $Root 'Office\Data'
    $arch = if ($Architecture -eq '64') { 'x64' } else { 'x86' }
    $versions = @(Get-ChildItem -LiteralPath $data -Directory -ErrorAction Stop |
        Where-Object { $_.Name -match '^16\.0\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending)
    foreach ($version in $versions) {
        $neutral = Join-Path $version.FullName "stream.$arch.x-none.dat"
        $localized = Join-Path $version.FullName "stream.$arch.$Language.dat"
        $cab = Join-Path $data "v$Architecture.cab"
        if ((Test-Path -LiteralPath $neutral) -and (Test-Path -LiteralPath $localized) -and (Test-Path -LiteralPath $cab)) {
            return [pscustomobject]@{ Root = $Root; Version = $version.Name; Architecture = $Architecture; Language = $Language }
        }
    }
    throw "Midia incompleta: Office\Data deve conter v$Architecture.cab e uma versao com os arquivos stream.$arch.x-none.dat e stream.$arch.$Language.dat."
}

function New-OfficeConfiguration {
    param([Parameter(Mandatory)]$Media)
    $doc = New-Object System.Xml.XmlDocument
    $configuration = $doc.CreateElement('Configuration')
    [void]$doc.AppendChild($configuration)
    $add = $doc.CreateElement('Add')
    $add.SetAttribute('SourcePath', $Media.Root)
    $add.SetAttribute('OfficeClientEdition', $Media.Architecture)
    $add.SetAttribute('Version', $Media.Version)
    $add.SetAttribute('Channel', 'Current')
    $add.SetAttribute('AllowCdnFallback', 'False')
    [void]$configuration.AppendChild($add)
    $product = $doc.CreateElement('Product')
    $product.SetAttribute('ID', 'O365ProPlusRetail')
    [void]$add.AppendChild($product)
    $language = $doc.CreateElement('Language')
    $language.SetAttribute('ID', $Media.Language)
    [void]$product.AppendChild($language)
    # Preserva documentos abertos e instalacoes existentes. Nao remove Office/MSI.
    foreach ($setting in @(@('FORCEAPPSHUTDOWN','FALSE'), @('AUTOACTIVATE','0'))) {
        $property = $doc.CreateElement('Property')
        $property.SetAttribute('Name', $setting[0])
        $property.SetAttribute('Value', $setting[1])
        [void]$configuration.AppendChild($property)
    }
    $display = $doc.CreateElement('Display')
    $display.SetAttribute('Level', 'None')
    $display.SetAttribute('AcceptEULA', 'TRUE')
    [void]$configuration.AppendChild($display)
    return $doc.OuterXml
}

Export-ModuleMember -Function Get-OfficeMedia, New-OfficeConfiguration
