# PostgreSQL lokaal gebruiken

De API gebruikt altijd PostgreSQL. In-memory is alleen een expliciet samengestelde testadapter.
Domain en Application kennen geen EF Core, Npgsql of Aspire.

## Starten

Vereisten: de .NET SDK uit `global.json` en een draaiende Docker-compatible container runtime.
AppHost en de integratietests gebruiken `postgres:17.11-bookworm`.

Stel eenmalig een eigen wachtwoord in via AppHost user secrets:

```powershell
dotnet user-secrets set "Parameters:postgres-password" "<eigen-lokaal-wachtwoord>" --project src/Aigenterra.AppHost
dotnet run --project src/Aigenterra.AppHost --launch-profile http
```

AppHost start PostgreSQL met database `aigenterra` en volume `aigenterra-postgres-data`.
De API wacht op de database en ontvangt de connection string via Aspire. Bewaar wachtwoord
en volume bij herstarts. Een ander wachtwoord wijzigt niet automatisch de credentials in
een bestaande PostgreSQL-datadirectory. Verwijder het volume niet om een startupfout te repareren.

Startup controleert de exacte gereviewde migration-ID's voor `InitialExecutionPersistence`,
`RecentTaskExecutionsIndex` en `CompleteStoppedCodexTurns`. Die laatste corrigeert eerder opgeslagen
Running Codex-turns/agents met een Stop-observation naar Completed. Een onbekende pending migration
stopt startup vóór uitvoering van enige pending migration; review SQL en tests voordat de allowlist
wordt uitgebreid. `Database:ApplyMigrationsOnStartup` bepaalt automatische uitvoering: standaard
aan in Development en uit daarbuiten, expliciet aan in de self-hosted Compose-stack.
Database- of migrationfouten veroorzaken geen fallback naar in-memory.

Self-hosted gebruikt dezelfde migrations, maar een afzonderlijk volume en lokale `.env`-configuratie;
zie de [README](../../README.md). Aspire user secrets blijven uitsluitend developmentconfiguratie.

## Migrations

DbContext, design-time factory, migrations en snapshot staan in Infrastructure. De factory
start geen Api/AppHost. Onderstaande commando's worden vanuit de repositoryroot uitgevoerd.

```powershell
dotnet tool restore
dotnet ef migrations add <Naam> --project src/Aigenterra.Infrastructure --startup-project src/Aigenterra.Infrastructure --context ExecutionDbContext --output-dir Executions/Postgres/Migrations
dotnet ef migrations script --idempotent --project src/Aigenterra.Infrastructure --startup-project src/Aigenterra.Infrastructure --context ExecutionDbContext --output artifacts/execution-migrations.sql
```

Review gegenereerde migrations en SQL. Maak zo nodig de outputdirectory `artifacts` vooraf aan.
Voor een expliciete lokale database-update stel je `ConnectionStrings__aigenterra` in op
de connection string van de draaiende database (via lokale secrets/environment, nooit in Git):

```powershell
dotnet ef database update --project src/Aigenterra.Infrastructure --startup-project src/Aigenterra.Infrastructure --context ExecutionDbContext
```

Migrationgeneratie en SQL-generatie hebben geen databaseverbinding nodig. Een database-update
vereist een echte connection string; de design-time localhost-default bevat geen wachtwoord.
Gebruik geen `EnsureCreated`, `EnsureDeleted` of automatische schema-diff. Destructieve changes
vereisen een aparte, expliciete migratiestap. Er wordt geen in-memory data geïmporteerd.

## Verificatie

```powershell
dotnet restore Aigenterra.slnx
dotnet build Aigenterra.slnx --no-restore
dotnet format Aigenterra.slnx --verify-no-changes --no-restore
dotnet test Aigenterra.slnx --no-build --no-restore
```

Infrastructure-tests gebruiken Testcontainers met een gedeelde tijdelijke PostgreSQL-container
en een afzonderlijke database per scenario. Ze gebruiken de echte migrations en afzonderlijke
connections/DbContexts voor concurrency. Docker is vereist; database-tests worden niet stilzwijgend overgeslagen.

Herstartproef: registreer een Codex UserPromptSubmit, noteer de interne IDs en timeline,
herstart eerst Api en vervolgens AppHost met hetzelfde volume. Dezelfde runtime-reference moet
naar dezelfde IDs verwijzen; herlevering mag geen tweede observation of sequence toevoegen.
Inspecteer evidence via databasegereedschap, niet via een nieuw publiek endpoint.

UTC-tijden zijn opgeslagen als .NET ticks (100 ns sinds 0001-01-01 UTC). `last_sequence` is
uitsluitend een atomische Infrastructure-counter. Timeline-reads laden geen evidencebytes.
Raw evidence blijft opaque `bytea`; de bestaande transportlimiet is 1 MiB. De generieke
observation-HTTP-route serialiseert haar JSON-input al vóór opslag; alleen de Codex-hookroute
levert rechtstreeks de oorspronkelijke hookbytes. SessionStart en onbekende hooks krijgen geen nieuw archief.

Codex-ingestion blijft uit afzonderlijke idempotente stappen bestaan. Een onderbreking kan
gedeeltelijk opgeslagen state achterlaten; een herlevering kan vervolgen. Collector blijft
fail-open zonder queue of automatische retry. Persistent volume betekent behoud bij herstarts,
geen backupvoorziening. Payloads en databasecredentials horen niet in logs.
