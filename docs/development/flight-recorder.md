# Flight Recorder lokaal ontwikkelen

De eerste UI bestaat uit `/executions` en `/executions/:id`. De lijst toont maximaal
50 recente executions; bewaar een detail-URL om een oudere execution later te openen.
Conversation is metadata. Raw evidence wordt alleen als aanwezig aangeduid.

## Starten

Vereisten: .NET uit `global.json`, Docker, Node 24 LTS >= 24.15 en npm.
Angular en zijn CLI staan projectlokaal; een globale Angular-installatie is niet nodig.
Gebruik de bestaande PostgreSQL-secret en het bestaande volume zoals beschreven in
[postgresql.md](postgresql.md).

Vanuit de repositoryroot:

```powershell
dotnet run --project src/Aigenterra.AppHost --launch-profile http
```

AppHost start PostgreSQL, Api, npm ci en de Angular-developmentserver. Open het HTTP-endpoint
van **web** in het Aspire-dashboard. De webpoort wordt door Aspire toegewezen; bookmark
bij herstarts zo nodig hetzelfde executionpad op het nieuwe webendpoint.

De browser vraagt `/api/task-executions` en `/api/task-executions/{id}/timeline` op.
De developmentproxy leest `API_HTTP` uit de door AppHost geïnjecteerde environment en verwijdert
alleen het `/api`-prefix. ASP.NET houdt zijn bestaande routes. Browsercode gebruikt geen Aspire
service discovery of PostgreSQL-connectionstring. `PORT` wordt platformonafhankelijk door het
Node-startscript aan Angular doorgegeven. De webserver bindt lokaal op 127.0.0.1.

Voor los frontendwerk, met een reeds draaiende API:

```powershell
cd src/Aigenterra.Web
npm ci
$env:API_HTTP = 'http://localhost:5248'
npm start
```

De override geldt alleen in deze shell. Zonder API_HTTP stopt de devserver met een uitleg.
Een toekomstige self-hosted webproxy kan dezelfde relatieve `/api`-routing en SPA-fallback
aanbieden; deze slice bevat geen productiecontainer, Compose of SSR.

## Semantiek en grenzen

- Status is de laatst geregistreerde backendstatus. Codex Stop is geen completion.
- Timelinevolgorde blijft de servervolgorde (ObservedAt, dan Sequence). Sequence is ingestvolgorde;
  ObservedAt is Collector-observatietijd, niet een gereconstrueerde runtimebrontijd.
- Lokale tijd wordt met tijdzone getoond; exacte timestamps blijven in metadata beschikbaar.
- Alleen expliciete parent-IDs maken een relatie. Codex-runtimekind bepaalt uitsluitend het
  presentatielabel Root agent/Subagent; ontbrekende lineage wordt niet ingevuld.
- De timeline-API retourneert voorlopig alles. De browser rendert in stappen van 100 observations.
  Dit begrenst de DOM, niet het dataverkeer. Refresh is handmatig.

## Verificatie

Stop AppHost vóór een volledige .NET-herbouw om Windows-bestandslocks te voorkomen.

```powershell
dotnet restore Aigenterra.slnx
dotnet build Aigenterra.slnx --no-restore
dotnet format Aigenterra.slnx --verify-no-changes --no-restore
dotnet test Aigenterra.slnx --no-build --no-restore
cd src/Aigenterra.Web
npm run build
npm test -- --watch=false
npm run format:check
```

Angular-tests controleren navigatie, requestannulering, fout/herstel/lege states, lineage,
Stop/status, servervolgorde, exacte tijden, veilige tekstweergave en bijladen. De bestaande
Application- en PostgreSQL-testprojecten testen limitvalidatie, SQL-projectie, stabiele sortering
en aantallen zonder evidence te laden. Er is geen extra API-testproject of Playwright-infrastructuur.

Handmatige browseracceptatie:

1. Controleer overzicht en detail op 1440x900, 1280x720, 768x1024 en 390x844; controleer ook 200% zoom.
2. Gebruik lange objectives, ononderbroken IDs, duizenden summarytekens, veel agents en 1.000 observations.
   Controleer dat niets overlapt, bediening minimaal 40px hoog is, volledige inhoud bereikbaar is en
   de pagina geen horizontale scroll nodig heeft. Gebruik fixtures buiten de echte recorderdatabase.
3. Controleer toetsenbordbediening, agent-/sectielinks, exact-metadata, Show more/less en Show 100 more.
4. Controleer lege data, ongeldige/ontbrekende execution, serverfout en API-uitval; Retry moet herstellen.
5. Start Aspire en voer een echte Codex-taak uit met de vertrouwde repositoryhooks. De Collector
   moet gebouwd zijn; hij gebruikt standaard `http://127.0.0.1:5248/ingest/codex/hooks`.
6. Refresh de lijst, open de nieuwe execution en inspecteer de echte agents en observations.
7. Stop en herstart AppHost zonder het PostgreSQL-volume te verwijderen. Open dezelfde execution opnieuw.

HTTP-contractsmoke: `/task-executions?limit=1` geeft `{items:[...]}`; limit 0 en 101 geven 400
Problem Details met `invalid_execution_limit`; een onbekend timeline-ID geeft 404 met
`task_execution_not_found`. Controleer dit ook via de webproxy.
