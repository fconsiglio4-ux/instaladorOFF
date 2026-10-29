# Instalador Office — Maquim

Instalador Windows com interface para instalar **O365ProPlusRetail** a partir
de IMG/ISO local ou pasta extraída. A imagem permanece ao lado do EXE e não
é incluída no repositório nem no artefato de compilação.

## Baixar e usar

Em **Actions → Compilar instalador Windows**, abra uma execução concluída
e baixe o artefato **Maquim-Office-Setup**. Extraia o ZIP, coloque sua imagem
na mesma pasta e execute `Maquim-Office-Setup.exe` como administrador.
Uma única imagem nessa pasta é selecionada automaticamente. Escolha a
arquitetura e o idioma disponíveis na mídia; padrão: 64 bits, português Brasil.

A mídia precisa conter `Office/Data/v64.cab` (ou `v32.cab`) e uma pasta de
versão com os fluxos neutro e de idioma correspondentes. Uma mídia incompleta
é rejeitada antes de iniciar a instalação. Como a imagem ainda não foi
fornecida para inspeção, sua compatibilidade deve ser confirmada num piloto.

## Fluxo

1. Valida privilégios, aplicativos abertos e arquitetura de Office existente.
2. Valida a assinatura Microsoft do ODT incorporado ao EXE.
3. Monta a imagem como ISO somente leitura ou usa a pasta selecionada.
4. Identifica a versão local e gera XML para `O365ProPlusRetail` com origem
   local, versão explícita e `AllowCdnFallback=False`.
5. Executa ODT e verifica código de saída, produto e arquitetura no registro.
6. Se marcado, baixa a revisão fixa do Ohook, verifica SHA-256 e executa a
   opção não interativa. Registra separadamente o resultado informado pelo script.
7. Desmonta somente imagens montadas por esta execução, inclusive após erros.

O processo não remove Office anterior, não força fechamento de aplicativos
e não reinicia o Windows. Saída 3010 adia a ativação até reinicialização.
Arquiteturas conflitantes e outras incompatibilidades precisam de resolução
manual. Não altera políticas permanentes de execução do PowerShell.

## Ativação selecionável

A interface identifica **Ohook 3.12**, conforme o fluxo solicitado. Esse método
altera componentes de licenciamento do Office; o sucesso informado pelo script
não equivale a uma verificação de licença Microsoft. Para ativação oficial por
conta licenciada, desmarque essa opção e entre na conta no aplicativo Office.

Não é usado `irm | iex` nem a versão mutável do servidor. Fonte externa fixada:

- Repositório: https://github.com/massgravel/Microsoft-Activation-Scripts
- Revisão: `6401d2f36975dc07bcb6d22ea823a8498a75ec01`
- Arquivo: `MAS/Separate-Files-Version/Activators/Ohook_Activation_AIO.cmd`
- SHA-256: `9c77ecce70a4c7161589dbbe20d9282a0b5071dba6260abb59ca35a765f475ef`

O script externo é baixado em tempo de execução, não redistribuído neste
repositório. Sua licença e código estão no projeto de origem. O instalador
não adiciona exclusões de antivírus. Erros de download, integridade ou resultado
interrompem essa etapa e preservam o Office já instalado.

## Compilar

No Windows 10/11 com .NET Framework 4.8 e Windows PowerShell 5.1:

```powershell
./tests/Test-Media.ps1
./scripts/Build.ps1
```

`Build.ps1` baixa o Office Deployment Tool oficial **16.0.20430.20146**,
valida assinatura Microsoft do pacote e do `setup.exe`, e incorpora este
último e os scripts no executável. A internet é necessária na compilação.
Saída: `dist/Maquim-Office-Setup.exe`, instruções e SHA-256. Não é necessário
instalar .NET SDK nem bibliotecas externas. O EXE Maquim não é assinado.

## Validação e limites

CI Windows valida a sintaxe PowerShell, seleção da versão local, XML, produto,
idioma, arquitetura, caminhos com espaços/`&` e rejeição de mídia incompleta.
Depois compila C#. Não instala Office nem executa Ohook no runner.
O teste real de montagem, instalação e ativação requer sua mídia e um
computador piloto. A configuração local não desativa as atualizações futuras
do Office; elas podem usar internet após a instalação.

Referência de configuração Microsoft:
https://learn.microsoft.com/pt-br/microsoft-365-apps/deploy/office-deployment-tool-configuration-options
