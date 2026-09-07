# Flight Recorder — acceptatie 7 september 2026

## Uitgevoerd

- .NET 10.0.400: restore, build, format-check en alle 79 tests geslaagd; geen buildwarnings.
- Angular: production build, 11 gerichte behavior tests en Prettier-check geslaagd.
- Aspire start PostgreSQL, Api, npm-installer en web. `/api`-proxy levert de bestaande backendroutes.
- HTTP-smoke via webproxy: recent limit=1 geeft het verwachte DTO; limit=0/101 geeft 400
  Problem Details; onbekende execution geeft 404 Problem Details. Geen extra API-testproject.
- Echte Codex CLI-taak uitgevoerd: AGENTS.md gelezen met één shell-toolcall. Execution
  `01a07c6e-0978-7c1e-941b-2c596113c37b` bevat één rootagent, vier observations en evidence-indicaties.
  UserPromptSubmit, PreToolUse, PostToolUse en Stop zijn zichtbaar. Status blijft Running na Stop.
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
