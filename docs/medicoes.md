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

Não verificado (ou verificado só parcialmente):

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
