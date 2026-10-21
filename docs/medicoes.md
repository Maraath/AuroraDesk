# Medições de consumo

Geradas por `tools\medir.ps1` (60 s, app publicado em Release com ReadyToRun). CPU em % do total de núcleos.

| Data | Rótulo | Private Bytes (MB) | Working Set (MB) | WS privado (MB) | CPU ociosa (%) | Pico CPU 1 s (%) | Handles | Threads | Início (ms) |
|---|---|---|---|---|---|---|---|---|---|
| 2026-10-21 15:30 | M1 - só bandeja (custo base) | 140,1 | 168,2 | n/d | 0,00 | 0,13 | 554 | 13 | 1841 |
| 2026-10-21 15:33 | M1 - bandeja, render por software | 55,1 | 73,0 | 14,8 | 0,00 | 0,00 | 419 | 12 | 628 |
| 2026-10-21 15:39 | M2 - temas e fontes (sem widgets) | 56,4 | 75,3 | 15,7 | 0,00 | 0,13 | 432 | 13 | 1471 |
| 2026-10-21 15:51 | M3 - 4 widgets (relógio, sistema, rede, bateria) | 61,3 | 96,5 | 19,8 | 0,06 | 0,39 | 483 | 18 | 1222 |
