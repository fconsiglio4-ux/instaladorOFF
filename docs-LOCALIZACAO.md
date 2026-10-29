# Localização do computador — 1.3.0

O agente tenta usar uma posição recente do Windows; quando não há posição/permissão/sessão, usa a localização aproximada do IP público. Se ambas falharem, envia os demais dados com localização indisponível. Nunca inventa latitude, longitude, precisão ou horário. A instalação e a fila do Office continuam independentes.

## Componentes

- `LocationHelperProgram.cs`: auxiliar .NET Framework com GeoCoordinateWatcher de alta precisão, executado na sessão do usuário. Usa as permissões normais de localização do Windows; não modifica configurações de privacidade do sistema. Fica visível na área de notificação, com Configurar localização, Atualizar agora, configurações do Windows e Sair.
- No primeiro login após instalar, apresenta a opção **Usar localização do Windows** ou **Somente IP público**. A escolha fica no HKCU desse usuário. O modo Windows permite o pedido normal de permissão; consultas periódicas não voltam a solicitar permissão. Usuários diferentes configuram sua própria escolha. Fechar o diálogo não concede acesso.
- O auxiliar consulta a cada cinco minutos e aceita somente uma posição válida com horário original de até 15 minutos, precisão finita e latitude/longitude válidas. Não usa altitude, velocidade, Wi-Fi SSIDs nem outras informações de pessoas.
- `LocationPipe.cs`: ponte de mensagens locais limitadas a 4 KiB/5 segundos entre o auxiliar e o serviço. Acesso de rede é negado; usuários autenticados locais podem enviar apenas dados de localização, não ler token/configuração. O serviço valida e salva o cache em sua pasta protegida. Essa informação não é uma prova contra adulteração por usuários que controlam o equipamento.
- `LocationCore.cs`: seleção de origem, validação, cache, rate limit e consulta HTTPS ao provedor. O serviço continua como LocalService e funciona sem login: nesse caso utiliza a alternativa por IP quando não houver cache Windows recente.
- O instalador copia `Maquim-ComputerLocation-1.3.0.exe` para a pasta do agente e registra **MaquimComputerLocation** em `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`. O Windows inicia o auxiliar normalmente no login, sem elevar privilégios. O serviço não depende desse auxiliar para enviar IP/disco/status.

A primeira posição Windows válida, ou a perda de validade dessa posição, pode antecipar uma coleta do status em até um minuto. As coletas regulares continuam a cada 12 horas, além das mudanças de IPv4 local.

## IP público aproximado

Consulta feita pelo próprio equipamento a `https://ipwho.is/?fields=success,ip,latitude,longitude,city,region,country`, usando a conexão de saída dele. Não usa o IP do servidor Lovable e não envia token Maquim, serial, patrimônio ou coordenadas Windows ao provedor. O provedor recebe o IP público da conexão e o user-agent do agente.

Retém apenas IP público, latitude, longitude, cidade, região, país e instante da consulta. **Precisão em metros permanece null**, pois o provedor não informa raio de precisão. VPN/proxy, CGNAT e operadoras podem indicar outra região. Não identifica endereço de residência nem garante localização física.

Consulta limitada a uma a cada 15 minutos por computador, com reserva/cache persistentes; cache só vale na mesma rede local e até 15 minutos. HTTP 429 respeita Retry-After entre 15 minutos e 24 horas, com 24 horas como padrão. Sem token Maquim configurado, não faz consulta externa por IP; a fila continua funcionando. Timeout de 15 segundos, TLS 1.2, certificado normal, sem redirecionamentos e resposta limitada a 16 KiB. Testes usam respostas simuladas, sem consultar localização real externa.

IPWhois permite HTTPS e uso comercial no endpoint gratuito, com limite publicado de 1000 consultas/dia por IP cliente e sem SLA: https://ipwhois.io/documentation . Se a rede/quantidade de computadores ultrapassar esse limite, manter status indisponível e rever o provedor/plano com a TI; não há contratação automática.

## Dados enviados

O contrato `/api/public/computer-status` ganha apenas o campo opcional `location` (agentes antigos continuam aceitos). Todos os subcampos são explícitos:

- `source`: `windows`, `public_ip` ou `unavailable`.
- `latitude`, `longitude`: números válidos ou null se indisponíveis.
- `accuracy_meters`: estimativa do Windows, ou null para IP/indisponível.
- `captured_at`: ISO 8601 com fuso, horário original da posição Windows ou da consulta por IP; null se indisponível.
- `public_ip`, `city`, `region`, `country`: consulta por IP; null nas outras origens.
- `windows_status`: `available`, `permission_denied`, `unavailable`, `no_session`, `stale` ou `error`.

O tamanho máximo do status continua 16 KiB. A fila conserva integralmente esse objeto, ID, sequência e data em todos os reenvios; não atualiza localização durante reenvio. O contrato de instalação de Office, com seus oito campos, não muda.

## Publicação e homologação

1. No repositório Roteiro, confirmar a migração de monitoramento `20261029201224_computer_monitoring.sql` aplicada.
2. Aplicar `supabase/migrations/20261029205326_computer_location.sql` no SQL editor do Lovable Cloud e publicar o Roteiro atualizado **antes de instalar a nova versão do agente**. Sem isso, a API antiga pode rejeitar o novo campo (422), preservando registros com `.blocked` até correção e revisão da TI.
3. Instalar o Office Setup 1.3.0 e manter o token em Configurar integração. O fallback por IP começa com as coletas do serviço. Para Windows, fazer novo login ou abrir normalmente o auxiliar da pasta instalada pelo Explorador; escolher Usar localização do Windows e atender às permissões normais do sistema.
4. Em Instalações → Computadores → Monitoramento, verificar fonte, coordenadas, precisão, horário e Abrir no mapa. O mapa é um link externo OpenStreetMap; só abre ao clicar, sem carregar mapas/rastreadores de terceiros automaticamente.
5. Em um computador real autorizado, testar permissão aceita/negada, localização Windows desativada, sem Wi-Fi/GPS, sem usuário conectado, offline, VPN, retorno da rede e reinício. Precisão real não pode ser validada em runner sem sensores; a API Windows pode produzir posição grosseira mesmo em modo High. O painel sempre mostra a precisão informada, sem rotular como GPS.

Quando o status novo não contém localização válida, o painel conserva **Última localização conhecida** com data/origem/precisão originais, distinguindo-a da coleta indisponível. Duplicatas/seqüências atrasadas não substituem essa posição por valores antigos.

## Compilar, testar e remover

`scripts\Build.ps1` compila instalador, serviço e auxiliar, todos sem token/senha embutidos. `LocationTests.cs` cobre prioridade Windows, fallback, permissão, dados antigos/futuros, ausência de precisão de IP, intervalos, 429, cache, HTTPS sem Authorization e ponte local real. O teste de serviço continua sem token, portanto não consulta IP nem cria registros fictícios em produção.

Para remover o auxiliar, a TI pode excluir o valor **MaquimComputerLocation** do caminho Run acima e encerrar o auxiliar pela área de notificação. A remoção do serviço segue docs-MONITORAMENTO.md. Não apagar filas pendentes sem decidir sua destinação. A assinatura digital comercial dos novos componentes permanece pendente, assim como no instalador anterior.
