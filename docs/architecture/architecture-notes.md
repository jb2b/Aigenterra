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

**Context:** Instrumentation mag een Codex-taak niet blokkeren en moet zonder credentials of runtime-SDK kunnen werken.

**Keuze:** De repo-lokale hook start een vooraf gebouwde, kortlevende framework-dependent Collector die stdin byte-identiek via HTTP doorstuurt, nooit payloads logt en altijd succesvol eindigt. Er is nog geen queue of retry.

**Consequentie:** De integratie blijft klein en ontkoppeld, met bewust geaccepteerd telemetryverlies wanneer de lokale API niet beschikbaar is.
