# Token incorporado e conta local Maquim — 1.1.1

O token de ingestão autorizado pelo responsável está incorporado em
um recurso binário na compilação, tanto no instalador quanto no agente. No primeiro
uso, ele é gravado automaticamente com DPAPI para a conta Windows atual. Não
é necessário digitá-lo. Configuração manual e variável de ambiente permanecem
como substituições explícitas; uma configuração existente não é sobrescrita.

Antes de iniciar o Office, após o técnico conferir serial e patrimônio, o
instalador prepara **a conta local Maquim**, habilitada e pertencente ao grupo
Administradores. A associação usa o SID `S-1-5-32-544`, independente do idioma
do Windows. Usa `PrincipalContext(ContextType.Machine, Environment.MachineName)`;
não cria usuários de domínio nem modifica políticas de senha.

A senha definida pelo responsável é incorporada durante a compilação. A senha
não é enviada ao Roteiro, não aparece em argumentos de comando, tarefa agendada,
fonte público ou logs. O agente de reenvio não recebe esse recurso de senha.

Se Maquim já existir, a senha atual é preservada. A associação ao grupo é
verificada/adicionada. Uma conta existente desabilitada causa uma falha explícita;
não é reativada automaticamente. A interface identifica esse comportamento antes
de executar. Não há redefinição silenciosa de senhas de contas existentes.

Se a criação falhar por política do Windows ou por falta de permissão, a
instalação do Office não começa. Se a conta foi criada nesta execução, mas a
associação ao grupo falhar, tenta remover apenas essa conta recém-criada. Não
exclui contas preexistentes. Se a conta foi criada com sucesso e o Office falhar
mais tarde, a conta permanece e o resultado está em `account.log`.

## Única configuração necessária para a compilação final

No repositório `fconsiglio4-ux/instaladorOFF`:

1. **Settings → Secrets and variables → Actions → New repository secret**.
2. Cadastre dois segredos: `INSTALLATIONS_INGEST_TOKEN` e `MAQUIM_LOCAL_ADMIN_PASSWORD`.
3. Valores: o token de ingestão fornecido e a senha solicitada para a conta Maquim, respectivamente.
4. Salve e execute novamente **Actions → Compilar instalador Windows**.

O plugin não fornece escrita em segredos do GitHub. Portanto, essa configuração
precisa ser feita pelo responsável autenticado, uma única vez. Não publique a
senha em um commit, issue ou comentário.

O workflow sempre compila e executa os testes simulados, com uma senha fictícia
exclusivamente de teste. Esse executável de teste é apagado e nunca é publicado.
Sem ambos os segredos configurados, o workflow sinaliza a pendência e não publica EXE
final. Quando configurado, compila e publica o artefato normalmente.

Na compilação, a senha é lida do ambiente, gravada em arquivo temporário ignorado
pelo git e passada como recurso binário ao compilador. O arquivo temporário é
apagado no `finally`; o valor não é passado na linha de comando do compilador.

Localmente no Windows, defina `INSTALLATIONS_INGEST_TOKEN` e `MAQUIM_LOCAL_ADMIN_PASSWORD` apenas no ambiente
seguro do processo de build. Execute:

```powershell
./tests/Test-Media.ps1
./scripts/Build.ps1 -TestOnly
./scripts/Build.ps1
```

Quem obtiver o EXE poderá extrair seus recursos, inclusive a senha incorporada.
O segredo do GitHub evita a exposição no fonte/histórico; não torna a senha
inextraível do executável. Distribua o EXE somente às pessoas autorizadas.

## Validação

Os testes existentes de interface, identificação, fila e protocolo são mantidos.
Novos testes verificam preenchimento automático e substituições do token,
conferência da conta na interface, criação com a senha fornecida ao serviço,
associação ao grupo, preservação de conta existente, falha de política,
reversão da criação incompleta e ausência de senha nos resultados/logs.
São usados provedores simulados; nenhuma conta real é criada no GitHub Actions.
O teste real de login da conta deve ser feito no computador piloto autorizado.

A incorporação foi expressamente reconfirmada pelo responsável após a
explicação do risco de extração das credenciais do executável.
