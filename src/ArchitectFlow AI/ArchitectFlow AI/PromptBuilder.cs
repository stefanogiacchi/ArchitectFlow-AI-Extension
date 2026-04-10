using System.Collections.Generic;
using System.Linq;

namespace ArchitectFlow_AI
{
    public class PromptBuilder
    {
        public string Build(string acContent, string wikiContent, List<ProjectContext> targets)
        {
            return $@"
        [ROLE] Agisci come Senior Software Architect .NET esperto in CQRS e Dapper.
        [CONTEXT] Sto lavorando sulla soluzione {targets[0].Namespace.Split('.')[0]}.
        [INFRASTRUCTURE] 
        - Base Class: ApiBase (Namespace.Base)
        - Error Handling: ExceptionMiddleware (Global)
        - DB: Dapper con DapperMapperClass (Fabric per Connection)
        - Transaction: IUnitOfWork per CUD.

        [INPUT DATA]
        - Acceptance Criteria: {acContent}
        - SQL Design: {wikiContent}

        [TASK] Genera il codice completo dividendo i file per i progetti selezionati:
        {string.Join(", ", targets.Select(t => t.Name))}
        
        Assicurati che:
        1. Ogni AC sia coperto da un FluentValidation e un Unit Test.
        2. La query SQL venga salvata in un file .sql separato.
        3. L'Handler usi UnitOfWork.Commit() solo se tutte le operazioni riescono.";
        }
    }
