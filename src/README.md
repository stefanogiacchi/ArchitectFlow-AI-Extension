# ⚡ ArchitectFlow AI — v2.0

Estensione VSIX per **Visual Studio Community 2022** che automatizza lo scaffolding di
API basate su pattern **CQRS · Dapper · Mediator**, con integrazione diretta nella
**Solution Explorer** e sistema di **file di riferimento vincolanti**.

---

## 🆕 Novità v2.0 — Solution Explorer Integration

### 📎 Selezione multipla dalla Solution Explorer

```
Ctrl+Click su più file → Tasto destro →
"Aggiungi selezione a ArchitectFlow References" →
I file appaiono nel pannello e diventano contesto vincolante
```

| Gesto | Effetto |
|---|---|
| Tasto destro su un **file** | Aggiungi file singolo |
| **Ctrl+Click** su più file → tasto destro | Aggiungi tutti i file selezionati |
| Tasto destro su una **cartella** | Aggiungi tutti i file sorgente (ricorsivo) |
| Tasto destro su un **progetto** | Aggiungi tutti i file del progetto |
| **Trascina** file nel pannello | Drag & drop diretto |
| Click **+ Sfoglia file…** nel pannello | File picker classico |

### 🔒 Reference come contesto vincolante

Quando ci sono file di riferimento selezionati, il prompt inviato all'AI include
automaticamente:

1. **Lista dei file** con path relativo e linguaggio
2. **Contenuto completo** di ogni file (configurabile)
3. **Istruzioni esplicite** a Claude di seguire:
   - Stesse naming convention (namespace, classi, metodi)
   - Stessi pattern architetturali (CQRS, Mediator, Dapper)
   - Stessa struttura cartelle e namespace
   - Stessi package NuGet già usati
   - Stili di documentazione XML doc

---

## 🏗 Architettura del codice v2.0

```
src/ArchitectFlow AI/
│
├── ArchitectFlowPackage.cs          ← Entry point VSIX (AsyncPackage)
│                                       Inizializza servizi, comandi, Tool Window
│
├── ArchitectFlow.vsct               ← Tabella comandi VS
│                                       Menu contestuale Solution Explorer
│                                       (file, cartella, progetto, multi-select)
│
├── source.extension.vsixmanifest   ← Manifest VSIX
│
├── Commands/
│   ├── AddToReferencesCommand.cs   ← Comando principale
│   │                                  Usa IVsMonitorSelection + IVsMultiItemSelect
│   │                                  per leggere la selezione multipla reale
│   └── OtherCommands.cs            ← OpenPanel, ClearReferences
│
├── Models/
│   └── ReferenceFile.cs            ← Modello file di riferimento
│                                      (path, linguaggio, size, contenuto)
│
├── Services/
│   ├── ReferenceFileManager.cs     ← Gestione stato reference files
│   │                                  ObservableCollection WPF-bindable
│   │                                  BuildContextPayload() → payload AI
│   └── ClaudeApiService.cs         ← Chiamate API Claude con streaming SSE
│                                      6 modalità di generazione specializzate
│
└── ToolWindows/
    ├── ArchitectFlowToolWindow.cs   ← Host del pannello
    ├── ArchitectFlowToolWindowControl.xaml      ← UI WPF
    └── ArchitectFlowToolWindowControl.xaml.cs   ← ViewModel + code-behind
```

---

## ⚙️ Configurazione

**Strumenti → Opzioni → ArchitectFlow AI**

| Impostazione | Default | Descrizione |
|---|---|---|
| API Key Anthropic | *(vuota)* | Chiave API per Claude. Ottienila su [console.anthropic.com](https://console.anthropic.com) |
| Modello Claude | `claude-sonnet-4-20250514` | Modello da usare |
| Includi contenuto file | `true` | Invia il contenuto completo dei file di riferimento all'AI |
| Max file di riferimento | `30` | Limite massimo di file nel pannello |

---

## 🎯 Modalità di generazione

| Modalità | Cosa genera |
|---|---|
| **CQRS Command** | `record Command` + `IRequestHandler` + `Validator` FluentValidation |
| **CQRS Query** | `record Query` + `DTO` + `IRequestHandler` con Dapper |
| **API Endpoint** | Route ASP.NET Core + validazione + dispatch MediatR + Swagger |
| **Repository (Dapper)** | Interfaccia `IRepository` + implementazione con `IDbConnection` |
| **Dapper SQL Query** | Query SQL parametrizzate + mapping C# |
| **MediatR Handler** | `IRequestHandler` con DI, logging, gestione errori dominio |
| **Freeform** | Risposta libera con codice pronto per la produzione |

---

## 🔧 Build & Installazione

### Prerequisiti
- Visual Studio 2022 (Community, Professional o Enterprise)
- .NET Framework 4.7.2+
- Workload "Sviluppo di estensioni per Visual Studio" installato in VS

### Build
```
1. Apri ArchitectFlow AI.sln in Visual Studio 2022
2. Restore NuGet packages
3. Build → Build Solution (F6)
4. L'output è in bin/Debug/ArchitectFlow AI.vsix
```

### Debug
```
F5 → Avvia l'istanza sperimentale di Visual Studio con l'estensione caricata
```

### Deploy
```
Doppio click su ArchitectFlow AI.vsix per installare
```

---

## 📝 Utilizzo rapido

1. **Apri** il pannello: `Strumenti → ArchitectFlow AI`
2. **Seleziona file** di riferimento nella Solution Explorer con Ctrl+Click
3. **Tasto destro** → "Aggiungi selezione a ArchitectFlow References"
4. I file appaiono nel pannello con l'indicatore `● N file vincolanti`
5. **Scrivi il prompt**: es. "Genera CreateInvoiceCommand con validazione importo > 0"
6. **Ctrl+Invio** o click **✨ Genera**
7. Il codice rispetta esattamente i pattern dei file selezionati

---

## 📄 Licenza

MIT — Stefano Giacchi  
[github.com/stefanogiacchi/ArchitectFlow-AI-Extension](https://github.com/stefanogiacchi/ArchitectFlow-AI-Extension)
