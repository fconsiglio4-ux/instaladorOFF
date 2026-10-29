# Agente Maquim 1.2.0 — monitoramento de computadores

A instalação do Office confirmada continua gravando seu resultado na fila anterior. Em seguida, o instalador instala o serviço `MaquimComputerMonitor`, visível como **Maquim - Monitor de Computadores**. Uma falha do agente gera aviso separado; não desfaz o Office nem apaga sua fila. A confirmação antes da instalação informa a instalação do agente.

## Operação

- Serviço Windows .NET Framework 4.8, conta **LocalService**, sem senha de usuário, início automático atrasado e recuperação por reinício após falha. Independe do login do técnico ou de sessão interativa.
- Coleta inicial e depois a cada **12 horas** desde a coleta anterior, enquanto o computador estiver ligado. Suspensão/desligamento não permitem executar; ao voltar, coleta o status vencido. Não há promessa de duas entregas em um computador desligado ou sem internet.
- A cada minuto verifica o conjunto de **IPv4 locais ativos** dos adaptadores (incluindo VPNs ativos), excluindo loopback/APIPA/túneis. Não consulta IP público, localização nem usuários. Endereços ordenados e sem duplicatas evitam alertas por reordenação dos adaptadores. Mudança do conjunto antecipa uma coleta; não espera as 12 horas.
- Reaproveita a série e o patrimônio informados e conferidos pelo técnico. Preserva zeros e letras. Não coleta chaves de produto.
- Discos fixos prontos: unidade, capacidade total e espaço disponível em bytes. Não coleta nomes nem conteúdo de arquivos. Discos removíveis/de rede não entram.
- `machine_id` UUID persistente identifica o computador; reinstalar o Office no mesmo Windows mantém o UUID e atualiza o vínculo `installation_id`. Uma nova instalação do Office continua com seu próprio UUID. Instalar este agente **depois de clonar/imaging/Sysprep**, em cada equipamento: não distribuir uma imagem com config/fila do agente já preenchida.

## Arquivos e token

Binário: `%ProgramFiles%\Maquim\ComputerMonitor\Maquim-ComputerMonitor.exe`.
Configuração, fila, estado e diagnóstico: `%ProgramData%\Maquim\ComputerMonitor`.

O token é fornecido em **Configurar integração** no instalador; não está no EXE, no GitHub Actions nem em argumentos de processos. O instalador transfere o token para DPAPI **LocalMachine**, necessário porque o serviço roda como outra conta. O diretório de dados permite acesso somente a Administradores, SYSTEM e LocalService. A fila anterior de instalações permanece em LocalAppData com seu token CurrentUser e tarefa agendada: não muda de dono nem de rota.

Após salvar/corrigir o token no instalador, o token do serviço já instalado também é atualizado. Sem token, preserva status offline. Não imprimir token ou conteúdo de `token.dpapi` em chamados/logs.

## Envio e fila

`POST https://roteiro-maquim.lovable.app/api/public/computer-status`, HTTPS/TLS 1.2 com certificado validado, timeout de 30 segundos e redirecionamentos desabilitados. Usa `INSTALLATIONS_INGEST_TOKEN`, nunca credenciais de banco. Contrato de até 16 KiB:

`event_id`, `machine_id`, `installation_id`, `sequence_number`, `observed_at` (ISO 8601 com fuso), `computer_name`, `serial_number`, `asset_tag`, `agent_version`, `ip_addresses` (array de strings) e `disks` (array de `{name,total_bytes,free_bytes}`).

Gravação atômica antes do envio. Reenvia a cada 15 minutos e ao iniciar serviço, preservando ID, sequência, instante e conteúdo. Só remove com HTTP 200/201 e JSON `ok: true` mais `event_id` correspondente. `duplicate: true` exige a mesma confirmação. 401 pausa até corrigir token; 400/413/415/422 geram sidecar `.blocked`, preservando JSON e interrompendo repetição daquele registro; outros erros/rede/timeout mantêm para depois. Limite de 1000 pendentes: preserva os existentes e suspende novas coletas até drenar. Não mistura filas de Office, impressoras e status.

Registros bloqueados exigem revisar/atualizar o agente ou a API e, somente após corrigir a causa, remover o respectivo `.blocked` com o serviço parado. Não editar IDs/instantes dos JSON pendentes.

## Publicação do Roteiro Maquim

1. Aplicar `supabase/migrations/20261029201224_computer_monitoring.sql` do repositório `fconsiglio4-ux/roteiro-maquim` no SQL editor do Lovable Cloud.
2. Manter o segredo server-side `INSTALLATIONS_INGEST_TOKEN` já usado pela API de instalações.
3. Publicar o Roteiro atualizado. A API de status é independente do contrato antigo; não altera os oito campos da instalação.
4. Só então fazer o teste integrado com um equipamento real autorizado. Confirmar `Instalações → Computadores → Monitoramento`, espaço, último recebimento e alerta ao trocar IP.

A tela atualiza a cada minuto. Última comunicação é o recebimento do status mais recente pelo servidor; "Coletado" mostra o instante original, distinguindo filas antigas. "Comunicação recente" significa recebimento há até 26 horas, e não presença online em tempo real. Alertas incluem IP anterior, atual, instante, quantidade e histórico paginado. Duplicatas e relatórios atrasados não regridem o status nem geram alertas duplicados. Permissão de leitura reutiliza Instalações para usuário ativo/admin; somente o servidor grava.

## Compilar e testar

Windows: `powershell -NoProfile -File scripts\Build.ps1`. Gera `dist\Maquim-Office-Setup.exe` com o serviço embutido. O build executa os testes anteriores e `MonitorTests.exe` com HTTP simulado. Não incorpora credenciais e não cria usuário real nos testes.

GitHub Actions também executa `tests\Test-MonitorService.ps1 -ServiceExecutable .\build\Maquim-ComputerMonitor.exe` em runner descartável. Esse teste instala o serviço real usando o mesmo código do instalador, valida LocalService, coleta de discos, início automático e fila após reiniciar o serviço, e o remove. Não existe token no teste; portanto nenhum status fictício é enviado à produção. **Não executar esse teste de provisionamento em um equipamento com agente existente.**

Homologação manual em VM: reiniciar o Windows e confirmar serviço em execução sem login; testar offline, posterior retorno da conexão, token incorreto/correção, IP diferente, múltiplos adaptadores e discos. Testes automatizados de 12 horas usam relógio simulado, sem esperar 12 horas.

Executável e serviço continuam sem assinatura comercial; essa mudança não resolve bloqueios do Controle Inteligente de Aplicativos. Para distribuição sob essa proteção, assinar e validar todos os componentes executados com certificado confiável.

## Administração/remoção

Verificar: `Get-Service MaquimComputerMonitor`; diagnóstico sem credenciais: `Get-Content "$env:ProgramData\Maquim\ComputerMonitor\status.txt"`.

Para remover, em PowerShell administrativo:

```powershell
Stop-Service MaquimComputerMonitor
sc.exe delete MaquimComputerMonitor
```

Após confirmar remoção, a TI pode apagar a pasta do binário; conservar dados pendentes até decidir sua destinação. Remover o serviço não altera o Office nem o histórico no Roteiro. Os dados e o token só são apagados mediante ação explícita da TI.

## Atualização 1.3.0

O agente também envia localização Windows ou aproximada pelo IP público, respeitando permissões e identificando a origem e o horário. Isso adiciona o campo opcional location na rota de status e um auxiliar de sessão; ver [docs-LOCALIZACAO.md](docs-LOCALIZACAO.md). Os contratos e as filas anteriores continuam separados.
