# Architecture notes

Dit document bevat alleen beslissingen die de structuur of ontwikkelrichting van Aigenterra blijvend beïnvloeden. Elke notitie beschrijft kort de context, keuze en consequenties.

## 2026-09-04 — Dependencies wijzen naar de kern

**Status:** Accepted

**Context:** Aigenterra wordt opgebouwd volgens DDD, Clean Architecture en ports and adapters.

**Keuze:** Domain kent geen andere Aigenterra-projecten. Application kent Domain. Infrastructure implementeert later de door concrete use-cases gevraagde ports. Hosts en adapters mogen uitsluitend naar binnen wijzen.

**Consequentie:** Technische details kunnen veranderen zonder het domein of de use-cases afhankelijk te maken van hosting, opslag of frameworks. Architecture tests bewaken de directe projectreferences.

## 2026-09-04 — Hosting staat buiten de Clean Architecture-kern

**Status:** Accepted

**Context:** De API moet lokaal via Aspire worden gestart en geobserveerd.

**Keuze:** Api is de HTTP-host en composition root. AppHost orkestreert alleen resources. ServiceDefaults bevat uitsluitend gedeelde hosting-, observability-, health- en service-discoverydefaults.

**Consequentie:** Geen van deze projecten bevat domeinbeleid. Domain en Application refereren nooit naar Api, AppHost of ServiceDefaults.

## 2026-09-04 — Het domeinmodel ontstaat vanuit use-cases

**Status:** Accepted

**Context:** Er is nog geen concrete Aigenterra-use-case of uitgewerkte ubiquitous language.

**Keuze:** Er worden nu geen aggregates, entities, value objects, domain events, repositories of persistence-abstractions ontworpen.

**Consequentie:** Domain, Application en Infrastructure mogen in het fundament leeg zijn. De eerste echte use-case introduceert alleen de begrippen en ports die dan aantoonbaar nodig zijn.

## 2026-09-04 — Execution-hiërarchie gebruikt vier aggregates

**Status:** Accepted

**Context:** Een Conversation kan veel tasks, agents en duizenden observations bevatten.

**Keuze:** Conversation, TaskExecution, AgentExecution en Observation zijn zelfstandige single-entity Aggregate Roots. De conceptuele hiërarchie wordt met immutable typed IDs vastgelegd, niet met geneste collecties.

**Consequentie:** Een observation registreren vereist geen grote objectgrafiek. Regels over relaties tussen aggregates worden door concrete Application use-cases gecontroleerd.

## 2026-09-04 — Executions bestaan onafhankelijk van experimentation

**Status:** Accepted

**Context:** Aigenterra moet één willekeurige execution kunnen observeren en inspecteren zonder formeel Experiment.

**Keuze:** Iedere TaskExecution hoort bij precies één Conversation, maar geen van de executionconcepten kent Experiment, Variant of Trial.

**Consequentie:** Een toekomstige analytische laag kan executions uitsluitend via IDs refereren en wordt nooit eigenaar van de execution-data.

## 2026-09-04 — Runtime-correlatie en raw evidence blijven gescheiden

**Status:** Accepted

**Context:** Externe runtimes leveren eigen IDs en provider-specifieke payloads.

**Keuze:** Een providerneutrale RuntimeReference is een Domain value object. Raw source payloads blijven opaque Application/Infrastructure-data en worden niet in Domain geïnterpreteerd.

**Consequentie:** Het executionmodel kan bronnen correleren zonder afhankelijk te zijn van Codex, Claude, JSON of een persistenceformaat.

## 2026-09-04 — Terminale status sluit observability niet af

**Status:** Accepted

**Context:** Een completion-event kan vóór vertraagde telemetry arriveren.

**Keuze:** TaskExecution en AgentExecution bewaren een expliciete actuele status, maar observations blijven na een terminale status toegestaan. Completion cascaded niet naar agents.

**Consequentie:** Current state blijft goedkoop opvraagbaar terwijl out-of-order en onvolledige telemetry correct kan worden vastgelegd.

## 2026-09-04 — Codex-integratie is een anti-corruptielaag

**Status:** Accepted

**Context:** Codex hookpayloads en runtimebegrippen mogen het generieke executionmodel niet bepalen.

**Keuze:** Aigenterra.Adapters.Codex parseert Codex-payloads en vertaalt ze rechtstreeks naar bestaande Application-use-cases. Domain en Application kennen geen Codex-schema.

**Consequentie:** Wijzigingen in Codex blijven aan de adaptergrens; er ontstaat geen generiek eventschema zonder concrete use-case.

## 2026-09-04 — Stop-signalen zijn niet autoritatief

**Status:** Accepted

**Context:** Een Codex Stop- of SubagentStop-hook kan worden gevolgd door verdere uitvoering en bevat geen completed/failed-outcome.

**Keuze:** Stop en SubagentStop worden alleen immutable observations. Alleen Interrupt mag vanuit de huidige hookintegratie een execution op Interrupted zetten.

**Consequentie:** Normale hook-geobserveerde turns blijven eerlijk op Running totdat een latere autoritatieve bron een terminale outcome levert.

## 2026-09-04 — Ingestmetadata reconstrueert geen bronvolgorde

**Status:** Accepted

**Context:** Hooks kunnen asynchroon en in omgekeerde volgorde bij Aigenterra aankomen; Codex levert geen betrouwbare brontimestamp.

**Keuze:** ObservedAt is het lokale Collector-observatiemoment. Sequence is uitsluitend de atomische Aigenterra-ingestvolgorde.

**Consequentie:** De timeline sorteert deterministisch op ObservedAt en Sequence, maar claimt geen onbekende Codex source ordering.

## 2026-09-04 — Collector is een fail-open transportgrens

**Status:** Accepted

**Context:** Instrumentation mag een Codex-taak niet blokkeren en moet zonder credentials of Codex runtime-SDK kunnen werken. Collector-transport moet lokaal wel observeerbaar zijn zonder payloads prijs te geven.

**Keuze:** De repo-lokale hook start een vooraf gebouwde, kortlevende framework-dependent Collector die stdin byte-identiek via HTTP doorstuurt, nooit payloads logt en altijd succesvol eindigt. De Collector exporteert uitsluitend veilige transportmetadata via OpenTelemetry OTLP. Er is nog geen queue of retry.

**Consequentie:** De integratie blijft klein en ontkoppeld, met bewust geaccepteerd telemetryverlies wanneer de lokale API of het telemetry-endpoint niet beschikbaar is. OTLP-export en flush blijven begrensd en fail-open.

## 2026-09-07 — PostgreSQL implementeert Application-capabilities

**Status:** Accepted

**Context:** Execution-data moet API- en AppHost-herstarts overleven, terwijl in-memory bruikbaar blijft voor tests.

**Keuze:** De module Executions.Postgres binnen Infrastructure implementeert de bestaande write-, runtime-reference- en timeline-ports. Api kiest PostgreSQL via composition; Aspire orkestreert de database met een persistent volume. TryFinishTaskExecutionAsync en TryFinishAgentExecutionAsync beschrijven de benodigde terminale overgang, geen algemene Save-operatie.

**Consequentie:** Infrastructure is afhankelijk van Application omdat Application de benodigde capabilities bezit. Domain en Application blijven frameworkvrij. Een extra PostgreSQL-project levert nu onvoldoende isolatiewaarde; tests die Infrastructure refereren krijgen de technische packages transitief mee.

## 2026-09-07 — Persistence-entities beschermen de zelfstandige aggregates

**Status:** Accepted

**Context:** Domain gebruikt readonly constructors, typed IDs en RuntimeReference; ingestmetadata hoort al buiten Domain.

**Keuze:** Vier platte persistence-entities met expliciete mappings staan uitsluitend in Infrastructure. Domain-objecten worden via constructors en de bestaande Finish-regel hersteld; timelines worden rechtstreeks naar Application-readmodels geprojecteerd.

**Consequentie:** Er zijn kleine mappings te onderhouden, maar geen EF-constructors, tracking of navigation-collecties in Domain. Een relationele relatie of gedeelde database-transactie maakt zelfstandige aggregates niet tot één aggregate. Observations worden niet gebruikt om actuele state te reconstrueren: dit is geen event sourcing.

## 2026-09-07 — Database-atomiciteit ondersteunt execution-semantiek

**Status:** Accepted

**Context:** Gelijktijdige requests mogen geen dubbele feiten, verloren lifecycle-overgangen of onjuiste ingest-sequences veroorzaken.

**Keuze:** Append verhoogt last_sequence op de taskrij en schrijft observation plus evidence in één transactie. last_sequence is uitsluitend een persistence-mechanisme en komt niet in TaskExecution. Per resourcetabel bewaken unique runtime-reference-indexes identiteit; foreign keys bewaken relaties. Finish wijzigt alleen een nog Running opgeslagen execution. De timeline gebruikt intern een read-only REPEATABLE READ-snapshot.

**Consequentie:** Row locks serialiseren appends per task; een rollback verbruikt geen sequence. Application bepaalt de use-case en gebruikt Domain voor lifecycle-regels; databaseconstraints bewaken gelijktijdige writes. Verloren finish-races leiden tot opnieuw lezen en behoud van de eerste eindtijd. Isolation level en queryvorm zijn Infrastructure-details die onafhankelijk kunnen veranderen. Codex-hooks blijven afzonderlijke idempotente stappen, zonder volledige hooktransactie of nieuwe Unit of Work.

## 2026-09-07 — Exacte evidence en tijd, begrensde opslagkeuze

**Status:** Accepted

**Context:** Bronbytes moeten inspecteerbaar behouden blijven en .NET-tijdprecisie mag timeline-ordering niet veranderen.

**Keuze:** Infrastructure bewaart opaque evidence als bytea met media type op de observationrij en UTC-tijden als bigint-ticks. De bestaande transportlimiet van 1 MiB en evidence-scope blijven gelden; timelines tonen alleen aanwezigheid. Er komt geen retrievalendpoint of ontvangstarchief bij.

**Consequentie:** Bytefidelity en 100-ns-precisie blijven behouden, tegen minder direct leesbare SQL-tijden. bytea op observations is de huidige keuze voor kleine payloads, geen uitspraak dat toekomstige grote artifacts ook in PostgreSQL thuishoren. Er wordt geen JSON-kopie onderhouden en payloadlogging blijft uitgeschakeld.

## 2026-09-07 — Flight Recorder is een HTTP outer adapter

**Status:** Accepted

**Keuze:** Angular gebruikt expliciete API-contracten. Api vertaalt Application-readmodels naar HTTP-DTOs; Domain en Application kennen Angular en browserrouting niet.

**DDD / Clean Architecture:** De gebruikersinterface is een outer adapter. Een transportcontract is geen gedeeld Domain-model.

**Trade-off:** Kleine expliciete contractmappings blijven nodig, maar frontend en domein kunnen onafhankelijk veranderen.

## 2026-09-07 — Overzichtsreads ontstaan uit een concrete inspectiebehoefte

**Status:** Accepted

**Keuze:** Application bezit een begrensde recente-executions-read naast de bestaande timeline-read. PostgreSQL projecteert metadata en aantallen rechtstreeks zonder aggregates of evidence te laden.

**DDD / Clean Architecture:** Een readmodel mag meerdere aggregates beschrijven zonder ze als één aggregate te behandelen. Een UI-use-case rechtvaardigt een concrete port, geen generieke repository.

**Trade-off:** Een gespecialiseerde query en index vragen onderhoud; grote objectgrafieken en een algemeen queryframework blijven achterwege.

## 2026-09-07 — Browserrouting blijft onafhankelijk van procesdiscovery

**Status:** Accepted

**Keuze:** De browser gebruikt relatieve `/api`-URLs. Een webproxy vertaalt die naar de bestaande API-routes; Aspire levert tijdens development het API-endpoint aan het webproces.

**DDD / Clean Architecture:** Aspire is technische orchestratie buiten Domain/Application. Browser-URL-resolutie is geen Application-capability.

**Trade-off:** Development vereist proxyconfiguratie; een latere self-hosted webserver kan hetzelfde browsercontract behouden.
