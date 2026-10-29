# Instalar e atualizar somente o agente — versão 1.4.0

## Executáveis

O pacote compilado contém:

- **Maquim-Office-Setup.exe**: tela inicial com Instalar Office + agente, Instalar somente agente e Atualizar/reparar agente instalado.
- **Maquim-Agente-Setup.exe**: instalador independente, menor, com somente Instalar agente e Atualizar/reparar. Não contém ODT, imagem de Office, scripts de instalação/ativação, agendador de reenvio do Office nem código de criação da conta Maquim.

Ambos solicitam elevação administrativa para instalar/configurar o serviço. O serviço continua como LocalService; o auxiliar de localização continua na sessão do usuário sem elevação, respeitando as permissões do Windows.

## Nova instalação independente

1. Executar Maquim-Agente-Setup.exe e escolher **Instalar somente agente**.
2. Informar e conferir série (até 255 caracteres) e patrimônio (até 100). Campos obrigatórios, trim somente nas extremidades, preservando letras/zeros. Não usa chaves de produto nem copia serial para patrimônio.
3. Opcionalmente informar o token de comunicação. Sem token/internet, a fila é mantida para enviar depois.
4. Conferir os dois valores e a operação antes de executar. Voltar conserva o preenchimento.
5. O programa instala o serviço, configura início automático/recuperação, copia o auxiliar e registra seu início no login.

Não é necessário Office instalado, arquivo IMG/ISO, aceitar licença do Office nem fornecer senha para criar usuário. Esses modos não instalam/ativam Office e não criam a conta Maquim. Não geram registros fictícios de instalação de Office.

A configuração recebe um machine_id persistente e um installation_id de provisionamento do agente. Esse segundo UUID atende ao contrato de status; não implica que o Office foi instalado. O histórico Instalações do Office só recebe dados quando ocorrer e for confirmada uma instalação real. A tela Monitoramento recebe os computadores com ou sem Office normalmente.

Se já existe configuração/serviço, a instalação nova é bloqueada: usar atualização/reparo para não criar IDs sobre uma fila existente.

## Atualizar ou reparar

O executável independente seleciona atualização quando encontra configuração existente. Série e patrimônio são carregados em campos somente leitura e exibidos na confirmação. O programa confere novamente a identificação antes de executar, bloqueando alterações concorrentes entre a revisão e a execução.

A atualização preserva:

- config.json e machine_id/installation_id existentes, série e patrimônio;
- token.dpapi (vazio no campo mantém exatamente o arquivo salvo; informar novo token troca explicitamente a configuração);
- todos os JSON pendentes, IDs, instantes e conteúdo;
- arquivos .blocked, auth-paused, sequência e caches de localização;
- escolhas de localização de cada usuário no HKCU.

O serviço é parado brevemente para trocar os binários e iniciado ao terminar. Uma versão diferente gera nova coleta com sequência crescente; os registros antigos continuam intactos. Atualização não chama Configure nem gera novos IDs do agente. Se o serviço estiver ausente mas houver configuração válida, Atualizar/reparar recria o serviço usando os mesmos dados. Configuração inválida/inacessível não é tratada como instalação nova: é necessário corrigir/restaurar com a TI, sem sobrescrever a fila.

Recursos do pacote são verificados antes de parar o serviço. Em falha de implantação, o programa tenta recuperar o binário anterior e reiniciar o serviço que estava em execução. Não apaga fila/configuração para fazer rollback. Um mutex impede duas instalações/atualizações simultâneas. Se a operação falhar, conferir o estado em Serviços; a recuperação é melhor esforço, sem garantia contra bloqueio de arquivos/Windows.

O auxiliar em uso pode continuar na sessão atual; no próximo login, o caminho Run inicia a versão atualizada. Não força logout nem encerra aplicações do usuário.

## Token e reenvio

Token fornecido somente durante a execução, armazenado com DPAPI LocalMachine na pasta protegida do serviço. Nunca fica no EXE, linha de comando ou logs. O campo é mascarado e limpo depois da execução. Para configurar/corrigir depois, executar novamente Atualizar agente e informar o token. Um token corrigido remove a pausa de autenticação; os registros bloqueados por formato continuam sujeitos à revisão da TI conforme docs-MONITORAMENTO.md.

O modo independente não cria/usa a fila nem a tarefa agendada da instalação do Office. Seu status segue a rota `/api/public/computer-status`, mantendo as filas separadas de impressoras e Office.

## Compilar e testar

`powershell -NoProfile -File scripts\Build.ps1` gera os dois setups em dist e hashes SHA256 de ambos. Não há credenciais incorporadas. O build verifica os recursos/tipos da distribuição independente para confirmar ausência de instalação/ativação de Office e de criação de usuário.

AgentSetupTests.cs valida campos obrigatórios, navegação sem mídia Office, zeros/alfanuméricos, confirmação, campos protegidos na atualização, ID estável e bloqueio de configuração inválida.

O teste Windows real instala uma fixture do serviço com versão anterior, coleta offline sem token, atualiza para o pacote novo e confirma troca efetiva do binário (hash), serviço em execução, sequência crescente, config/IDs/token DPAPI e fila preservados. O blob DPAPI do teste contém texto vazio, impedindo chamadas externas; nenhum registro fictício é enviado à produção. A fixture usa o código de serviço com versão anterior simulada para exercitar a troca do executável, sem representar homologação de toda versão antiga já distribuída.

## Roteiro e publicação

Continua necessária a API de status atualizada e as migrações de monitoramento/localização no Lovable Cloud, conforme docs-LOCALIZACAO.md. Este modo não requer migração nova: o contrato do status continua igual. Publicar API/aplicar SQL antes da homologação integrada; nenhum SQL foi aplicado por este instalador. A aprovação para integrar os PRs à main continua pendente.
