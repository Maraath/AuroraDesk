# Medições de consumo

Máquina: notebook com Windows 11 Pro 26200, Intel UHD + NVIDIA RTX 3050 Laptop, .NET 10.0.13.
O app foi publicado em Release (ReadyToRun) e medido por `tools\medir.ps1`, que roda por 60 s. A CPU está em % do total de núcleos e a "CPU ociosa" é a média dos 10 s em diante.
A tabela de histórico fica **no fim do arquivo**, porque o script anexa uma linha a cada execução.

## Experimentos de runtime (`tools\experimentos.ps1`, 35 s, dock + 4 widgets)

| Variação | Private Bytes (MB) | Pico WS (MB) | CPU total (s) |
|---|---|---|---|
| padrão (runtimeconfig do M5) | 61,6 | 104,0 | 1,12 |
| GCgen0size = 1 MB | 61,8 | 105,3 | 1,20 |
| GCConserveMemory = 7 | 61,7 | 106,3 | 1,30 |
| **TieredCompilation desligada** (adotada) | **59,6** | **102,1** | **0,84** |
| TC_QuickJitForLoops desligado | 61,5 | 104,3 | 1,02 |
| GCHeapHardLimit = 32 MB | 61,9 | 105,7 | 1,30 |

Outros números medidos durante o desenvolvimento:

| Situação | Private Bytes |
|---|---|
| Só a bandeja com renderização **por GPU** (padrão do WPF) | 140,1 MB (os drivers NVIDIA/Intel são carregados no processo) |
| Só a bandeja com renderização **por software** | 55,1 MB |
| Dock com fundo do DWM (`DWMWA_SYSTEMBACKDROP_TYPE`, exige GPU) | 156,3 MB (descartado) |
| Dock com acrílico via accent policy (build Debug) | 68,6 MB, contra ~66 MB sem acrílico |
| Configurações abertas / depois de fechar (Debug) | 83,3 MB / 72,8 MB, contra 65,5 MB antes de abrir |

## Verificação

Testado de verdade nesta máquina, com capturas de tela e UI Automation:

- 106 testes automatizados do núcleo passando (`dotnet test`).
- Os 4 widgets aparecem, atualizam (o relógio vira o minuto) e acompanham a troca de tema na hora.
- Hot reload: editar `skin.json` recarrega em ~1 s; um JSON quebrado mantém a versão anterior com uma faixa de erro.
- Dock:
  - itens padrão e apps em execução com indicadores;
  - ampliação ao passar o mouse, com cursor posicionado por script;
  - reserva de espaço (AppBar), que reduz a área de trabalho de 1032 para 959 px e volta a 1032 ao sair;
  - acrílico.
- Configurações: as 6 páginas abrem; ao fechar, a memória é liberada.
- "Iniciar com o Windows" grava a chave Run e "Restaurar original" a remove e apaga o backup.
- O app fecha com `--sair` e tem uma instância só.
- **Reinício do Explorer e Win+D** (`tools\teste-area-de-trabalho.ps1`, com veredito pela ordem Z em `tools\ordem-z.ps1`). Passos: início → Win+D → Win+D de volta → `explorer.exe` encerrado e reaberto → Win+D → Win+D de volta.
  - Nos 7 passos, os widgets ficam logo acima do desktop e abaixo das janelas.
  - O app sobrevive ao reinício do Explorer e recria o ícone da bandeja.
  - O dock continua acima da nova barra de tarefas.
  - O teste encontrou **dois defeitos**, já corrigidos:
    1. No Win+D, o Explorer ativa o desktop antes de trazê-lo para a frente, e os widgets ficavam atrás. Correção: os widgets são reposicionados mais algumas vezes logo depois.
    2. Se o desktop já estava em primeiro plano (depois de um clique nele ou de reiniciar o Explorer), o Win+D não gera evento de foco. Correção: enquanto o desktop estiver em primeiro plano, e só nesse período, a ordem Z é conferida a cada 400 ms.

- **Barra do Windows oculta enquanto o app está aberto** (`tools\teste-barra.ps1`), 6 de 6 passos OK:
  - Sem o app: barra visível e área de trabalho até y=1032.
  - App aberto: barra oculta, "ocultar automaticamente" ligado e área de trabalho até y=1080.
  - Fechar normalmente, ou encerrar à força (o vigia restaura): barra visível e estado original (0) restaurado.
  - Reiniciar o Explorer com o app aberto: a barra nova é ocultada de novo assim que o Explorer termina de montá-la (~12 s neste PC). O teste encontrou um erro no temporizador desse passo, que já foi corrigido.
  - O teste de Win+D/Explorer com a barra oculta também passou (7 de 7).
  - Consumo: app 59,4 MB e vigia 5,8 MB de Private Bytes.
- **Dock reservando o espaço da barra oculta** (mesmo `tools\teste-barra.ps1`):
  - App aberto: área de trabalho até y=1015; as janelas maximizadas param acima do dock (conferido em captura).
  - Fechar ou encerrar à força: área de trabalho volta a y=1032.
  - O teste encontrou um defeito: depois de reiniciar o Explorer a reserva se perdia (y=1080), porque o novo Explorer esquece as AppBars. Corrigido: o dock se registra de novo e a área volta a y=1015.

- **Dock em três ilhas** (conferido em captura): ilhas alinhadas ao dock, textos alinhados, Win+A e Win+N abrem os painéis do Windows com a barra oculta, e a barra volta a sumir ao fechá-los.
  - Custo medido: +7,5 MB de Private Bytes (61,2 sem as ilhas, 68,5 com elas).
  - O volume lido pelo Core Audio a cada 2 s, recriando os objetos COM, custava 0,30% de CPU ociosa. Guardando o ponteiro do alto-falante (renovado a cada ~20 s), caiu para 0,14%. Sem o volume, a CPU fica em 0,09%.
  - Início: 1,4 a 2,5 s nesta sessão, mas também 1,8 a 2,0 s com as ilhas desligadas. O PC estava mais carregado (CPU em ~55%); em sessões anteriores, a mesma medição dava 0,8 s.

- **Seta ^ da bandeja**: três cliques reais com o mouse abriram o painel nativo de ícones ocultos, com Aurora Desk, Claude, Discord, Segurança do Windows e Remover hardware. Ele fechou com Esc nas três vezes (3 de 3).
  - Investigação antes de escolher esse caminho: `ITrayNotify` (CLSID 25DEAD04…) registra o callback sem erro, mas não entrega nenhum item no Windows 11 26200.
  - A árvore de UI Automation da `Shell_TrayWnd` oculta tem 0 elementos.

- **Windows com o tema do Aurora**:
  - Com o Mocha, o registro do usuário ficou assim: modo escuro (`AppsUseLightTheme` e `SystemUsesLightTheme` = 0), `ColorPrevalence` = 1 e o destaque `#CBA6F7`.
  - Ao fechar o app, os valores voltaram exatamente aos originais: modo claro, `ColorPrevalence` 0, destaque azul do Windows e `AutoColorization` removido de novo.
  - Faixa das abas do Explorer (captura com `PrintWindow`): no tom do Mocha `#313244` com o Aurora aberto, e no padrão sem ele. A cor continua lá depois de 6 s.
  - O teste encontrou um defeito: o Explorer redefine a cor da barra logo depois de abrir. Corrigido: a cor é reaplicada após 0,4 s e 1,5 s e sempre que a janela ganha o foco.

- **Grupos de atalhos**: o ícone com 4 miniaturas foi conferido em captura. O painel do grupo foi testado pelo usuário com o mouse real e aprovado.

- **Correções da lista de erros do usuário**:
  - Cores do Windows depois de fechar: com "devolver ao fechar" desligado (o novo padrão), o registro continuou com modo escuro, `ColorPrevalence` 1 e destaque `#CBA6F7` depois de `--sair` e ao reabrir.
  - Mudanças que "sumiam": ligar "Disco C:" pela UI Automation gravou o `config.json` na hora, e o valor sobreviveu a `--sair` e à reabertura.
    - Causa provável: durante as atualizações, versões diferentes do app gravavam o mesmo `config.json`, e uma versão mais antiga apagava os campos que não conhecia.
    - Correção: agora campos desconhecidos são preservados (`[JsonExtensionData]`, com teste), e o dock grava antes de redesenhar.
  - Atalhos dos grupos que não abriam: os 13 `.lnk`/`.url` originais tinham ido para a Lixeira.
    - O dock agora guarda uma cópia em `%APPDATA%\AuroraDesk\atalhos` ao fixar, e os itens antigos também ganham cópia ao iniciar.
    - Os 15 atalhos deste PC foram copiados da Lixeira para essa pasta e todos os 19 itens apontam para arquivos existentes.
  - Gerenciador de Tarefas em primeiro plano: ele é elevado, e o Windows (UIPI) descarta em silêncio as teclas simuladas (Win+A/N/B).
    - Correção: antes de simular teclas, se a janela da frente for elevada, o Aurora põe a própria janela na frente.
    - Não verificado com clique real.

- **Configuração que "não salvava" (causa real):** o Claude desktop é um app empacotado (MSIX). Todo processo aberto a partir dele, incluindo os terminais do Claude Code e as instâncias do Aurora abertas por eles, tem as gravações em `AppData` redirecionadas para `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\AuroraDesk`.
  - A configuração ajustada nessas instâncias (grupos, ilhas, atalhos) ia para a cópia do pacote. O Aurora aberto pelo logon lia o `AppData` real e voltava ao padrão.
  - O redirecionamento não é detectável pela identidade do pacote (`GetCurrentPackageFullName` responde "sem pacote" nos filhos), então o app agora procura cópias da própria pasta em `Packages\*\LocalCache` ao abrir e avisa no log.
  - Correção: a configuração foi levada da cópia para o `AppData` real, a cópia foi removida e o app passou a ser aberto com `tools\abrir.ps1` (via WMI, fora do pacote).
  - Consequência para medições anteriores: testes de configuração feitos com o app aberto a partir do Claude desktop valeram para a cópia do pacote. O estado da barra do Windows (`ABM_SETSTATE`) e o registro não são redirecionados.

- **Skins de imagens do dock e das ilhas** (skin Pedra, conferida em captura ampliada):
  - Moldura em 9 partes, encaixes nos ícones, quadro do jogador, botões redondos e medalhão do relógio.
  - O meio da moldura se repete sem esticar. A primeira versão esticava a pedra, que virava listras.
  - Custo com o app aberto há ~1 min (medido no processo, não com `medir.ps1`, para não abrir o app dentro do pacote do Claude): 69,0 MB de Private Bytes, contra 68,5 MB sem skin, e 0,28% de CPU ociosa em 30 s, com o PC carregado (RAM em 94%).
  - Pontas (grifos): o motor está pronto, mas a skin Pedra não traz imagem. Não testadas com imagem real.

- **Início com o Windows por tarefa agendada** (0.1.8):
  - Antes, pela chave Run: nos dois boots de 23/10 o dock ficou pronto 26 s e 24 s depois do logon (logon às 12:40:12 e 16:55:08 no log do Winlogon; dock pronto às 12:40:40 e 16:55:32). O app em si levava só 1,7 s; o resto era o atraso que o Windows impõe à chave Run.
  - Depois: a tarefa `AuroraDesk` (gatilho no logon, prioridade 4 = normal, `--logon`) foi criada pelo instalador em modo silencioso. A entrada da chave Run foi removida do registro real; o Claude desktop vê uma cópia virtual dela, conferida com `reg query` aberto via WMI.
  - `schtasks /Run /TN AuroraDesk` abriu o app com prioridade normal (8) e o dock pronto em 1312 ms. Uma segunda abertura com `--logon` saiu sem abrir as configurações.
  - Explorer encerrado à força e o app aberto com `--logon` logo em seguida: o Windows reabriu o Explorer sozinho em ~1 s. A bandeja e a AppBar falharam enquanto ele montava a barra e se recuperaram no `TaskbarCreated` (área de trabalho até y=1015).
    - Efeito colateral do teste: o Explorer novo subiu com "ocultar automaticamente" (o estado 0 devolvido 3 s antes não tinha sido gravado), e o backup da barra registrou 1 como original. O backup foi corrigido à mão para 0.
  - Ainda não medido num boot real: o tempo entre o logon e o dock pronto. O log agora registra "N s depois de ligar o PC" nos primeiros 10 minutos.
  - Consumo com o app aberto há ~1 min (no processo): 69,6 MB de Private Bytes e 0,10% de CPU ociosa em 30 s.

- **Grupos de atalhos mais rápidos** (0.1.8): o painel é reaproveitado entre aberturas, os ícones dos atalhos são carregados com o app ocioso e a memória não é mais esvaziada 3 s depois de fechar o painel. O log registra "Grupo 'X' aberto em N ms".
  - Cliques reais do usuário, com a RAM em ~94%: a primeira abertura (que cria o painel) levou 233 ms; as seguintes, de outros grupos, 38 ms e 32 ms. Não há medição da versão anterior para comparar, porque o log de tempo entrou junto com a mudança.
  - O menu de energia abriu. Nenhuma das opções foi acionada no teste.

Não verificado (ou verificado só parcialmente):

- Molduras em janelas novas do Explorer: o Windows deste PC abre pastas como abas da janela existente, então o caminho "janela nova" foi coberto pela reaplicação ao iniciar e ao ganhar o foco, não por uma janela nova de fato.

- Clique real com o mouse nos itens das ilhas, como abrir o Gerenciador de Tarefas e rolar o volume. Os atalhos Win+A e Win+N foram testados enviando as teclas.

- O caso "Finalizar árvore de processos" (app e vigia encerrados juntos) deixa a barra oculta até abrir o app de novo ou usar Restaurar original. O caso está documentado, mas não foi testado.

- Pausa ao bloquear a tela, monitor desligado e tela cheia: os gatilhos estão implementados com as notificações oficiais, mas não foram disparados manualmente.
- Arrastar arquivos para o dock, reordenar arrastando e os menus de contexto dos widgets e do dock: precisam de interação com o mouse real e não foram exercitados.
- Importação de um skin do Rainmeter pela interface: o conversor está coberto por testes, mas o botão não foi clicado de ponta a ponta.
- Clima com a rede ligada: o parser foi testado com uma resposta de exemplo, sem consulta real à API.
- Multi-monitor e DPI diferentes por monitor: a máquina de teste tem um único monitor.

## Histórico (`tools\medir.ps1`)

| Data | Rótulo | Private Bytes (MB) | Working Set (MB) | WS privado (MB) | CPU ociosa (%) | Pico CPU 1 s (%) | Handles | Threads | Início (ms) |
|---|---|---|---|---|---|---|---|---|---|
| 2026-10-21 15:30 | M1 - só bandeja (custo base) | 140,1 | 168,2 | n/d | 0,00 | 0,13 | 554 | 13 | 1841 |
| 2026-10-21 15:33 | M1 - bandeja, render por software | 55,1 | 73,0 | 14,8 | 0,00 | 0,00 | 419 | 12 | 628 |
| 2026-10-21 15:39 | M2 - temas e fontes (sem widgets) | 56,4 | 75,3 | 15,7 | 0,00 | 0,13 | 432 | 13 | 1471 |
| 2026-10-21 15:51 | M3 - 4 widgets (relógio, sistema, rede, bateria) | 61,3 | 96,5 | 19,8 | 0,06 | 0,39 | 483 | 18 | 1222 |
| 2026-10-21 16:03 | M4 - dock + 4 widgets | 62,1 | 101,5 | 23,0 | 0,10 | 0,65 | 507 | 20 | 812 |
| 2026-10-21 16:17 | M5 - dock + 4 widgets + configurações (fechada) | 62,1 | 25,2 | 8,1 | 0,05 | 0,65 | 506 | 19 | 725 |
| 2026-10-21 16:23 | M6 - final: dock + 4 widgets, TieredCompilation off | 60,2 | 16,5 | 6,4 | 0,03 | 0,26 | 497 | 17 | 804 |
| 2026-10-21 16:49 | Correção Win+D (vigia do desktop) | 59,3 | 20,7 | 7,3 | 0,06 | 0,65 | 502 | 18 | 722 |
| 2026-10-21 17:07 | Barra do Windows oculta + vigia | 59,4 | 13,8 | 5,3 | 0,03 | 0,13 | 507 | 18 | 954 |
| 2026-10-21 17:49 | Autocontido (runtime embutido, para o instalador) | 65,8 | 32,9 | 10,6 | 0,16 | 2,08 | 599 | 17 | 6050 |
| 2026-10-21 17:50 | Autocontido (2ª execução) | 59,0 | 18,4 | 6,3 | 0,04 | 0,26 | 505 | 17 | 2745 |
| 2026-10-21 19:41 | Dock em três ilhas (estatísticas + bandeja) | 68,8 | 42,8 | 15,4 | 0,30 | 5,28 | 733 | 23 | 2464 |
| 2026-10-21 19:44 | Três ilhas, volume com ponteiro reaproveitado | 68,5 | 29,2 | 13,3 | 0,14 | 0,52 | 742 | 24 | 2181 |
