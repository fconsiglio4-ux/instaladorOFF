# Instalações → Computadores — instalador 1.1.0

O instalador mantém o uso da mídia IMG/ISO/pasta, a escolha de idioma e
arquitetura, as verificações de Office e a ativação opcional existentes.
Acrescenta identificação obrigatória, confirmação e registro da instalação
confirmada no Roteiro Maquim. Não modifica o instalador nem a fila de impressoras.

## Preparação

A migração de Computadores e a publicação da API no Lovable Cloud foram
confirmadas pelo responsável nesta implementação. Isso não substitui a
validação com uma instalação real. Nenhum registro fictício é enviado à produção.

1. Baixe e extraia o artefato do GitHub Actions. Coloque a mídia junto do EXE.
2. Abra o instalador com a conta Windows que será usada para instalar e reenviar.
3. Em **Configurar integração**, informe o mesmo `INSTALLATIONS_INGEST_TOKEN`
   do Roteiro Maquim. Alternativamente, defina essa variável no ambiente antes
   de abrir o instalador; ela será importada para a configuração protegida.
4. Não use credenciais Supabase/banco. O token não é incluído no código,
   artefato de build, comando agendado, argumentos de processo ou logs.

O token é protegido com DPAPI `CurrentUser`. A configuração, fila e agente
ficam em `%LOCALAPPDATA%\Maquim\ComputerInstallations` da **identidade Windows
que executa o instalador**. Há ACL para essa identidade e SYSTEM. O agente
agendado usa essa mesma identidade com `InteractiveToken` e `LeastPrivilege`,
sem pedir UAC a cada reenvio. Se abrir o EXE com credenciais de outro
administrador, a configuração pertence a esse administrador; os reenvios
agendados exigem uma sessão ativa dessa conta. Para o fluxo normal, eleve
com UAC sob a própria conta do técnico, que precisa ter direitos de instalação.
Não copie o arquivo DPAPI para outra conta ou computador.

A ausência de token/internet não impede a instalação: o JSON fica pendente.
Falha em preparar a pasta persistente ou registrar a tarefa é apresentada antes
que uma nova instalação seja iniciada; corrija em Configurar integração.

## Identificação e confirmação

Serial e patrimônio são textos independentes, obrigatórios, com limites de
255 e 100 caracteres. Apenas `Trim()` nas extremidades; zeros, letras e
espaços internos são preservados. Nenhuma chave de produto nem serial do BIOS
é consultado/preenchido. O técnico informa e confere os valores.

**Avançar** valida os campos, a mídia e o aceite de licença. A segunda etapa
mostra serial, patrimônio, computador e opções da instalação. **Voltar** reutiliza
os mesmos controles e preserva o preenchimento. Cada clique confirmado para
uma nova execução real gera um UUID novo; tentativas de envio nunca geram UUIDs.

## Confirmação da instalação e coleta

O PowerShell grava `office-installed.id` somente depois de o ODT retornar
0/3010 e confirmar produto e arquitetura no registro. O marcador corresponde ao
UUID da execução. Depois de a etapa de instalação/ativação terminar, o C#:

- obtém o nome real por `Environment.MachineName`;
- consulta `Win32_OperatingSystem` (edição, versão e build) e `DisplayVersion`/UBR;
- lê `ProductReleaseIds`, `VersionToReport`, `Platform` e `InstallationPath`
  do Click-to-Run na visão correta do registro;
- exige `O365ProPlusRetail`, build válido, `WINWORD.EXE` no caminho instalado,
  versão de arquivo válida e arquitetura PE compatível com `Platform`;
- inclui a versão do instalador em execução (1.1.0) e a hora final local com offset.

A descrição inclui o build detectado do pacote e a versão real do Word, que
podem diferir dentro de uma atualização. A versão da mídia não é usada como
resultado presumido. Se qualquer coleta obrigatória falhar, não é criado um
registro concluído. A mensagem indica a falha sem remover o Office instalado.
Uma falha posterior na ativação não apaga uma instalação já confirmada: o
registro de instalação pode ser enviado, sem dados de ativação no JSON.

## Fila durável e protocolo

O corpo tem exatamente oito campos, strings, sem dados de autenticação ou
ativação: `installation_id`, `installed_at`, `computer_name`, `os_version`,
`office_version`, `serial_number`, `asset_tag`, `installer_version`.

O JSON UTF-8 é validado com limite de 16 KiB, gravado em arquivo temporário no
mesmo diretório com flush em disco, e movido atomicamente para
`queue/<UUID>.json` **antes** do primeiro POST. Um UUID existente nunca é
sobrescrito com conteúdo diferente. O próprio arquivo original é enviado em
todos os reenvios; ID, data e bytes não são regenerados. Arquivos `.tmp` não
são processados. Um lock de arquivo impede dois agentes de consumir a mesma fila.

Endpoint fixo: `https://roteiro-maquim.lovable.app/api/public/computer-installations`.
POST JSON com Bearer token, TLS 1.2, certificado validado pelo Windows, timeout
de conexão/leitura de 30 segundos e redirecionamentos desativados. O corpo da
resposta é limitado; não é persistido em logs.

Só remove o JSON em HTTP **200/201**, com `ok` booleano **true** e
`installation_id` correspondente. `duplicate:true` é aceito quando essas mesmas
condições se verificam. Confirmação perdida, ID diferente, timeout, HTTP 3xx,
503 ou outra resposta não confirmada preservam os bytes para nova tentativa.

- **401:** mantém a fila e pausa os envios até salvar um token corrigido.
- **400/413/415/422:** mantém o JSON e grava `<UUID>.json.blocked`. Não tenta
  esse registro continuamente. A causa aparece em `status.txt` e no sidecar.
  Após corrigir a causa no contrato/API, use Configurar integração e marque
  **Reavaliar registros bloqueados**. O reenvio mantém o mesmo conteúdo.
- JSON local inválido também fica bloqueado. Preserve o arquivo para análise;
  não invente dados de Office nem gere outro ID para tentar contornar o erro.
- Erros temporários: preserva e retoma na próxima execução. No máximo 20
  registros por passagem; uma falha temporária interrompe o lote para evitar
  múltiplos timeouts enquanto a rede está indisponível.

O agente fica em `agent/Maquim-ComputerSync.exe`, independente do EXE que está
na pasta do técnico. A tarefa `Maquim-Computadores-<SID>` dispara no login (30 s
após entrar) e a cada 15 min enquanto essa identidade tem sessão ativa. Também
há reenvio ao abrir o instalador e imediatamente após enfileirar uma instalação.
Mover/apagar o instalador original não remove o agente. Reiniciar o computador
não apaga a fila. Se a tarefa falhar ou o token for rejeitado, os arquivos permanecem.

O status final é **“Instalação concluída; envio dos dados pendente”** quando não
há confirmação. Falha de rede não desfaz a instalação nem obriga reinstalar.

## Build e testes automatizados

No Windows 10/11 com .NET Framework 4.8 e Windows PowerShell 5.1:

```powershell
./tests/Test-Media.ps1
./scripts/Build.ps1
```

O build incorpora o ODT oficial, os scripts e o agente sem token. Compila e
executa `tests/IntegrationTests.cs` contra os mesmos fontes da interface e
integração. Os testes injetam respostas HTTP; **não executam POST real**,
instalação, ativação ou criação de tarefa no computador do runner. O teste de
agendamento intercepta `Register-ScheduledTask` e valida o XML.

Cobertura: campos vazios/espaços/limites, zeros e alfanuméricos, navegação real
da interface e conferência, registro Office incompleto, versões e arquitetura
reais injetadas, parser PE, coleta Windows no runner, contrato exato, bytes,
fuso, fila offline, nova instância e processo separado recuperando fila/token,
UUID idempotente, lock concorrente, 200/201, duplicidade, confirmação perdida,
ID errado, 401 e retomada, 400/413/415/422 sem repetição, 503/timeout, rota,
redirecionamentos, timeout HTTP e gatilhos da tarefa.

A detecção do Office real só pode ser validada no piloto, pois o runner não
instala o Office. Os testes usam snapshots simulados para esses casos.

## Piloto integrado (dados reais)

Pré-requisitos já confirmados pelo responsável: migração aplicada, API
publicada. Configure o token correto na conta do técnico antes do piloto.

1. Preencha os dados reais, avance, confira e volte para validar preservação.
2. Faça uma instalação real com a mídia. Confira a versão detectada no log.
3. Verifique `queue`: arquivo só deve desaparecer após confirmação válida.
4. No Roteiro Maquim, abra **Instalações → Computadores** e compare computador,
   Windows, Office, serial e patrimônio. Não use dados fictícios em produção.
5. Para validar offline, use uma execução real com a opção de ativação online
   desmarcada; após concluir sem rede, confira o JSON persistente e a mensagem.
6. Feche o instalador, reinicie e entre na mesma conta. Restaure a rede. A tarefa
   deve enviar o mesmo UUID/horário sem exigir reabrir o instalador.
7. Confirme um único registro no painel. Não refaça a instalação só porque o
   envio está pendente. Testes de token incorreto e respostas inválidas devem
   permanecer simulados para não alterar configurações de produção.

Não foi realizada uma instalação real remota nem confirmada visualmente a
listagem em produção nesta implementação. Essa verificação depende do piloto
no computador Windows do técnico. Não foram criados registros fictícios.

## Arquivos alterados

- `src/Program.cs`: etapas, identificação, confirmação, configuração e envio.
- `src/ComputerIntegration.cs`: contrato, coleta, fila, DPAPI e HTTP.
- `src/SyncAgent.cs`, `src/ComputerSyncProgram.cs`: agente persistente e instalação.
- `src/Register-ComputerSync.ps1`, `src/sync.manifest`: tarefa e execução sem UAC.
- `src/Install-Office.ps1`: marcador exclusivo de instalação confirmada.
- `src/app.manifest`: versão 1.1.0.
- `scripts/Build.ps1`: compila/incorpora agente e executa testes simulados.
- `tests/IntegrationTests.cs`, `tests/Test-Media.ps1`: regressões e contrato.
- workflow, README, LEIA-ME e este documento: build, uso e piloto.
