# Flight Recorder — acceptatie 7 september 2026

## Uitgevoerd

- .NET 10.0.400: restore, build, format-check en alle 79 tests geslaagd; geen buildwarnings.
- Angular: production build, 11 gerichte behavior tests en Prettier-check geslaagd.
- Aspire start PostgreSQL, Api, npm-installer en web. `/api`-proxy levert de bestaande backendroutes.
- HTTP-smoke via webproxy: recent limit=1 geeft het verwachte DTO; limit=0/101 geeft 400
  Problem Details; onbekende execution geeft 404 Problem Details. Geen extra API-testproject.
- Echte Codex CLI-taak uitgevoerd: AGENTS.md gelezen met één shell-toolcall. Execution
  `01a07c6e-0978-7c1e-941b-2c596113c37b` bevat één rootagent, vier observations en evidence-indicaties.
  UserPromptSubmit, PreToolUse, PostToolUse en Stop zijn zichtbaar. Status bleef bij deze toenmalige
  acceptatie Running na Stop; de turnsemantiek is op 8 september gecorrigeerd naar Completed.
- Na stoppen en herstarten van AppHost is dezelfde execution in de browser opnieuw geopend;
  agent, status en vier observations zijn behouden. Het bestaande PostgreSQL-volume is behouden.
- Codex CLI is voor deze proef bijgewerkt van 0.152.0 naar 0.153.4: de oude CLI ondersteunde
  het ingestelde model niet. De eerste mislukte poging blijft als echte gedeeltelijke opname zichtbaar.

## UI/UX-controle

Handmatig geïnspecteerd in de ingebouwde Chromium-browser, met DOM-afmetingen als aanvullende
controle. Geen Playwright-infrastructuur of dependency aan de repository toegevoegd.

Een tijdelijke fixture-server buiten de repository serveerde dezelfde production build met
24 agents, 1.000 observations, lange objectives, ononderbroken IDs en summaries van duizenden
tekens. Deze fixtures zijn niet in de recorderdatabase geschreven.

- Overzicht en detail beoordeeld, inclusief 1440x900, 1280x720, 768x1024 en 390x844.
- Geen horizontale paginaoverloop of buiten het scherm vallende content in de stress-detailpagina.
  Alle zichtbare buttons/summaries waren ten minste 40 CSS-pixels hoog.
- Objective breekt af op smalle schermen; detail toont de volledige tekst. Lange summaries en
  exacte metadata kunnen uitklappen en vergroten de pagina zonder interne scrollvensters.
- 100 observations aanvankelijk zichtbaar; Show 100 more geeft 200 en meldt 800 resterend.
- Root, Subagent met onbekende parent, onbekende lineage en expliciete parentlinks zijn getest.
  Echte opname bevatte alleen een rootagent; subagentpresentatie is met fixtures en tests geverifieerd.
- Navigatiefout door het HTML-basepad gevonden en opgelost: agent-/sectielinks blijven op de
  executionroute. Regressietest toegevoegd. Sectielinks voorkomen veel scrollen bij grote agentlijsten.
- Lege state, niet-gevondenmelding, API-uitval met behoud van geladen data en succesvol Retry
  in de browser gecontroleerd. Enter-navigatie naar timeline en terug naar overzicht werkt.

## Resterende grenzen

- Echte 200%-browserzoom kon niet worden ingesteld via de beschikbare ingebouwde browser.
  Een effectieve viewport van 720px is wel gecontroleerd; dit vervangt geen volledige zoomtest.
- De lijst toont alleen de recentste 50 executions. De timeline wordt nog volledig opgehaald,
  ondanks incrementeel renderen. Realtime en raw-evidenceweergave ontbreken bewust.

Beoordeling: behoorlijk bruikbaar voor de vroege developer-inspectieflow, met leesbare componenten
en expliciete onvolledige telemetry. Een volledige zoomcontrole in een gewone desktopbrowser
blijft onderdeel van verdere handmatige acceptatie.

## Self-hosted Linux — acceptatie 8 september 2026

- .NET 10.0.400: restore, build zonder warnings, format-check en alle 82 tests geslaagd.
  Hieronder vallen startupmigraties op een lege database, idempotente herstart en afwijzing
  van een onbekende pending migration vóór uitvoering van eerdere migrations.
- Angular: production build, 11 tests en Prettier-check geslaagd.
- Linux-images voor Api en Web gebouwd; alle drie Compose-services healthy. Api en Web draaien
  non-root. Alleen Web publiceert localhost; PostgreSQL en Api hebben geen hostpoort.
- De geïsoleerde Compose-smoketest controleert migrations, SPA/deeplink, API-querystrings en
  Problem Details, caching, ontbrekende assets, hookherlevering, byte-exacte Unicode/CRLF-evidence,
  de 1 MiB-grens, down/up-retentie, database-uitval en Api-vervanging met dezelfde Web-container.
  Runtimewachtwoorden verschijnen niet in imageconfiguratie/buildhistory; logs bevatten geen
  testwachtwoord, promptpayload of querymarker. Testresources worden na afloop verwijderd.
- Echte Codex CLI 0.153.4-turn via de ongewijzigde Windows-repositoryhooks uitgevoerd tegen
  `http://127.0.0.1:8080/api/ingest/codex/hooks`. Eén shell-call voerde `dotnet --version` uit.
  Execution `01a08162-c8dd-7fbb-bc06-34023ef15242` en root-agent zijn Completed; UserPromptSubmit,
  PreToolUse, PostToolUse en Stop verschijnen als vier observations met evidence-indicatie.
- Overzicht, detail, root-agent en timeline in de browser geïnspecteerd. Na Compose down/up
  zonder volumeverwijdering waren de volledige timeline en de vier evidence-hashes identiek.
  Een echte browserreload op dezelfde detail-URL toonde opnieuw de opgeslagen execution.
- Aspire-regressie: AppHost startte PostgreSQL, Api en Angular. API-health was Healthy;
  de development-SPA en `/api/task-executions?limit=1` werkten via de developmentproxy.
- De echte opname is gemaakt in een afzonderlijke disposable acceptatiestack; er zijn geen
  bestaande volumes verwijderd of credentials gecommit. De gewone installatie vereist nog
  steeds een eigen lokaal `.env`-wachtwoord. Acceptatie betreft Linux/amd64 en de huidige
  Windows-Collector, geen certificering van andere CPU-architecturen of Unix-hooklaunchers.
