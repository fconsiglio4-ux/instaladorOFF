# Conta local Maquim — versão 1.1.2

A senha é informada pelo técnico durante a execução e não é incorporada ao
executável, carregada de segredos GitHub, escrita em arquivo ou enviada ao Roteiro.

Após a conferência de serial e patrimônio, ao clicar em Instalar Office:

1. O instalador consulta se existe a conta **local** Maquim.
2. Se não existir, mostra uma janela identificando que a conta terá permissões
   administrativas. O técnico informa a senha e a confirmação em campos mascarados.
3. Senha vazia, somente espaços ou confirmação diferente impedem avançar. O valor
   não é aparado; espaços em senhas válidas são preservados. O Windows aplica
   suas próprias políticas de complexidade/comprimento.
4. Cancelar fecha a janela sem criar conta nem iniciar Office, preservando os
   dados do técnico na etapa de conferência.
5. Após receber o valor, os campos são limpos. A senha é usada pela API local
   do Windows; o instalador descarta as referências após a operação.
6. A conta é criada habilitada e adicionada a Administradores pelo SID
   `S-1-5-32-544`, sem depender do idioma do Windows.

O backend usa `PrincipalContext(ContextType.Machine, Environment.MachineName)`.
Não cria contas de domínio, não redefine políticas do Windows e não envia a
senha por comandos PowerShell ou parâmetros de processo.

Se Maquim já existir, a senha atual é preservada. O instalador não pede uma
nova senha; verifica/adiciona a associação ao grupo. Conta existente desabilitada
causa falha explícita, sem reativação silenciosa. Se a conta desaparecer entre
consulta e criação, a falta de senha interrompe o fluxo e o técnico pode tentar
novamente, quando será solicitada a senha.

Falha de permissão/política impede iniciar Office. Se a conta foi criada nesta
execução mas a associação ao grupo falhar, tenta remover somente essa conta
recém-criada. Nunca remove conta preexistente. Se a conta foi preparada e a
instalação Office falhar depois, a conta permanece. `account.log` registra apenas
o resultado, sem a senha.

## Token de comunicação

O token de ingestão é informado em **Configurar integração** e salvo com DPAPI
CurrentUser, com ACL da identidade Windows usada pelo instalador. Isso permite
ao agente recuperar o token após o fechamento/reinício do programa. Não há
recurso de token ou senha no instalador nem no agente. A variável de ambiente
`INSTALLATIONS_INGEST_TOKEN` continua como opção de configuração local durante
a execução, sendo importada para o armazenamento protegido.

O workflow não consulta `INSTALLATIONS_INGEST_TOKEN` nem
`MAQUIM_LOCAL_ADMIN_PASSWORD` dos segredos do GitHub; não depende deles para gerar
novas versões. Segredos anteriormente cadastrados não são distribuídos pelo build.

## Testes

A criação/associação/rollback usa um provedor simulado nos testes. A interface
é testada com campos vazios, espaços, confirmação divergente, mascaramento,
preservação exata da senha, limpeza dos campos e ausência de valor após cancelar.
Nenhuma conta real é criada no runner. O build inspeciona recursos dos dois EXEs
para impedir incorporar credenciais. O login real deve ser validado no piloto.
