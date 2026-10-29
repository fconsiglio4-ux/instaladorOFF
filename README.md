# Versão atual 1.4.0 — instalação e atualização independente do agente

O pacote inclui **Maquim-Agente-Setup.exe** e três opções no instalador principal: Office + agente, somente agente e atualizar/reparar. Atualização preserva identidade, token e filas. Ver [docs-AGENTE-INDEPENDENTE.md](docs-AGENTE-INDEPENDENTE.md).

# Versão atual 1.3.0 — localização Windows com alternativa por IP público

Ver [docs-LOCALIZACAO.md](docs-LOCALIZACAO.md) para publicar a API, aplicar a migração e configurar a localização no Windows.

# Versão 1.2.0 — agente de monitoramento

Serviço Windows instalado após confirmar o Office, reaproveitando série e patrimônio. Envia IPv4 local e espaço de discos a cada 12 horas e em mudanças de IP, mesmo sem login. Instruções, publicação da API, testes e remoção: [docs-MONITORAMENTO.md](docs-MONITORAMENTO.md).

# Instalador Office — Maquim 1.1.2

Instala O365ProPlusRetail a partir de IMG/ISO ao lado do executável ou pasta
extraída, preservando o fluxo de instalação e a ativação opcional existentes.
Integra os resultados confirmados a **Instalações → Computadores** do Roteiro.

A versão 1.1.2 recebe as credenciais **durante a execução**. O token é configurado
em **Configurar integração** e salvo com DPAPI para a conta Windows atual. Ao
criar a conta local Maquim, a senha é solicitada em uma janela com confirmação.
Token e senha não são incorporados ao EXE nem exigidos pelo build.

## Baixar e usar

Em Actions → Compilar instalador Windows, abra a última execução bem-sucedida
e baixe o artefato Maquim-Office-Setup. Extraia o ZIP e mantenha a imagem local
junto de `Maquim-Office-Setup.exe`.

1. Abra o EXE com privilégios de administrador.
2. Em Configurar integração, informe o mesmo token de ingestão do Roteiro.
3. Preencha serial e patrimônio reais; avance e confira os valores.
4. Clique em Instalar Office. Se Maquim não existir, informe e confirme a senha.
5. Aguarde a instalação, a confirmação dos dados reais e o envio ou enfileiramento.

Uma conta Maquim existente tem sua senha preservada e sua associação ao grupo
Administradores verificada. A senha digitada não é guardada pelo instalador,
passada em linha de comando ou enviada ao Roteiro.

Uma única imagem na pasta é selecionada automaticamente. Escolha idioma e
arquitetura disponíveis na mídia (padrão 64 bits / pt-br). A mídia deve conter
Office/Data, catálogo e arquivos de versão completos. Não é enviada à internet.

## Fila e segurança

O serial e patrimônio são obrigatórios e preservam zeros/letras. O resultado
usa o nome real do computador, Windows e Office efetivamente detectados. Falha
na confirmação impede registrar uma conclusão presumida.

A fila persistente fica em `%LOCALAPPDATA%\Maquim\ComputerInstallations`, separada
das impressoras. Reenvia ao abrir, no login e a cada 15 minutos sob a mesma conta
com sessão ativa. Preserva UUID/data/conteúdo e só remove com confirmação válida
de HTTP 200/201, `ok:true` e UUID correspondente. 401 pausa até corrigir o token;
400/413/415/422 bloqueiam o registro para revisão; falhas temporárias mantêm a fila.

POST em HTTPS com TLS 1.2, certificado normal, timeout de 30 segundos e sem
redirecionamentos. Credenciais não aparecem nos logs nem no JSON de instalação.

## Compilar e testar

Windows 10/11 com .NET Framework 4.8 e Windows PowerShell 5.1:

```powershell
./tests/Test-Media.ps1
./scripts/Build.ps1
```

O build baixa o Office Deployment Tool oficial, verifica sua assinatura,
compila instalador/agente e executa testes simulados da mesma interface e código.
Não precisa de segredos GitHub nem credenciais no ambiente de build. Não cria
contas reais nem registros em produção nos testes. Valida também que instalador
e agente não contenham recursos de credenciais. Saída em `dist` com instruções
e SHA-256. O executável Maquim não possui assinatura de código.

## Documentação

- [Integração, contrato, fila e piloto real](docs-INTEGRACAO.md)
- [Criação da conta local e senha durante a execução](docs-CONTA-MAQUIM.md)

O piloto real deve confirmar montagem da imagem, instalação, login da conta e
exibição dos dados no Roteiro. O runner não instala Office nem cria a conta local.

## Versão 1.3.0 — localização Windows com alternativa por IP

Inclui auxiliar visível na sessão do usuário e localização aproximada por IP público quando a posição do Windows não está disponível. Origem, precisão e horário explícitos, última posição conhecida e link para mapa no Roteiro. Migração/publicação devem preceder a instalação: [docs-LOCALIZACAO.md](docs-LOCALIZACAO.md).
