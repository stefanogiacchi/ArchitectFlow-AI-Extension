# ArchitectFlow AI — VSIX Extension

## Struttura del Progetto

```
ArchitectFlowAI/
│
├── ArchitectFlowPackage.cs          ← Entry point VSIX (AsyncPackage)
├── ArchitectWindowCommand.cs        ← Comando menu Strumenti > ArchitectFlow AI
├── ArchitectFlowAI.csproj           ← Project file (net472 + VS SDK)
├── source.extension.vsixmanifest   ← Metadati VSIX
│
├── Models/
│   ├── ProjectInfo.cs               ← DTO progetto C# (nome, namespace, layer, path)
│   └── GenerationContext.cs         ← Contesto completo per PromptBuilder
│
├── Services/
│   ├── SolutionScanner.cs           ← Scansiona la Solution via EnvDTE
│   ├── LinkParserService.cs         ← HTTP scraping + estrazione AC e SQL
│   ├── PromptBuilder.cs             ← Assembla il prompt strutturato
│   └── FileManager.cs               ← Crea i file .cs nei progetti corretti
│
└── UI/
    ├── ArchitectWindow.cs            ← ToolWindowPane wrapper
    ├── ArchitectWindowControl.xaml   ← Interfaccia WPF (sezioni A, B, C)
    └── ArchitectWindowControl.xaml.cs ← Code-behind con logica eventi
```

## Setup (Visual Studio 2022)

### Prerequisiti
1. Installare il workload **"Visual Studio extension development"** dal VS Installer
2. Avere **GitHub Copilot** installato (opzionale, il prompt viene comunque copiato negli appunti)

### Prima build
```
1. Aprire ArchitectFlowAI.csproj in Visual Studio
2. Restore NuGet packages (automatico)
3. F5 → avvia l'istanza sperimentale di VS con l'estensione caricata
4. Menu: Strumenti > ArchitectFlow AI
```

### Come aggiungere il comando al menu
Aggiungere in `source.extension.vsixmanifest` → Assets la voce Menu, 
oppure modificare il file `.vsct` (da generare con il template VSIX wizard).

## Flusso d'uso

```
1. Aprire una Solution in VS
2. Aprire ArchitectFlow AI (Strumenti > ArchitectFlow AI)
3. [A] Inserire URL User Story e Wiki + eventuale PAT
4. [B] Premere "Scan Solution" → selezionare i progetti target
5. Premere "Analyze & Sync" → l'estensione scarica e analizza i link
6. Verificare l'anteprima del prompt
7. Premere "Generate Code" → il prompt viene inviato a Copilot Chat
```

## Prossimi passi

- [ ] Implementare il file `.vsct` per la registrazione del comando nel menu
- [ ] Aggiungere supporto Jira REST API v3 (JSON nativo invece di HTML scraping)
- [ ] Implementare parsing Confluence REST API (`/wiki/rest/api/content/{id}`)
- [ ] Aggiungere finestra di "Generated Files" con diff viewer
- [ ] Unit test per `LinkParserService` e `PromptBuilder`
