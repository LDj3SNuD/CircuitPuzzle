<img width="960" height="1032" alt="CircuitPuzzle" src="https://github.com/user-attachments/assets/e380d5c7-17cd-4ec0-9061-676e75026718" />

# Circuit Puzzle

Gioco a riga di comando in C# (.NET 9): chiudi il circuito giusto accendendo
tutti i LED rossi senza mai accendere quelli blu.

## Comandi

| Tasto | Azione |
| ----- | ------ |
| `1`–`5` | Attiva/disattiva uno switch |
| `R` | Reset (tutti gli switch OFF) |
| `N` | Nuovo circuito, stessa difficoltà |
| `F1` / `F2` / `F3` | Difficoltà: facile / media / difficile (nuovo circuito) |
| `Q` oppure `Esc` | Esci |

## Regole

- Ogni filo ha il colore della sua rete: **scuro = spento**, **chiaro = acceso**.
- Porte: **AND** = tutti gli ingressi, **OR** = almeno uno, **XOR** = numero dispari, **NOT** = inverte.
- Un LED blu acceso significa game over.
- Nessun livello si risolve con un solo switch e ogni soluzione è raggiungibile
  un'interruttore alla volta, senza mai attraversare uno stato perdente.

## Difficoltà

I tre livelli cambiano numero di switch, porte logiche, LED blu e mosse minime
della soluzione. Ogni circuito è deterministico: il seed mostrato nel footer
riproduce esattamente quel livello con `new Random(seed)`.

## Avvio

```bash
dotnet run --project CircuitPuzzle
```

## Nota

Se un circuito non viene visualizzato correttamente, ridurre lo zoom della Console usando `CTRL`+`-` e poi ripristinare il circuito premendo `R`.

## Crediti

Progetto sviluppato con l'assistenza di [chat.z.ai](https://chat.z.ai).
