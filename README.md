# Aigenterra Flight Recorder

Aigenterra legt agentexecutions vast zodat je achteraf tasks, agents en observations kunt
inspecteren. Een Conversation is één Codex-chat; een TaskExecution is één promptturn.
Dit is een early-stage Flight Recorder met handmatige refresh, zonder accounts of authenticatie.

De detailpagina ondersteunt ook **Deterministic Evaluation**: opgeslagen build-/testcommandresultaten
met evidence-links. Codex-hooks bevatten geen exitcode; voer na een turn het lokale capturecommand
uit en klik daarna Evaluate. Zie [gebruik, bewijsgrenzen en capture](docs/development/deterministic-evaluation.md).

## Self-hosted starten

Vereist: Git en een draaiende Docker Engine met Linux-containers en Docker Compose v2.20+
(of nieuwer). Op Windows: Docker Desktop in Linux-container mode. Node en de .NET SDK
zijn voor de centrale stack niet lokaal nodig. De eerste build vereist internettoegang.

Na het clonen, vanuit de repositoryroot:

```powershell
Copy-Item .env.example .env
# Zet POSTGRES_PASSWORD in .env op een eigen, willekeurig wachtwoord.
docker compose up -d
```

Op Linux kan `cp .env.example .env` worden gebruikt. Genereer bijvoorbeeld 32 willekeurige
bytes als hex met `openssl rand -hex 32`, of met PowerShell 7:
`[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`.
Bewaar de waarde lokaal. Gebruik bij andere wachtwoorden dotenv-single-quotes om `$` en
`#` letterlijk te houden; commit `.env` nooit.

Open [Flight Recorder](http://127.0.0.1:8080/executions) zodra de stack healthy is.
`up -d` start op de achtergrond; gebruik `docker compose up -d --wait --wait-timeout 180`
om ook op readiness te wachten.

| Lokale setting | Default |
| --- | --- |
| `POSTGRES_PASSWORD` | Verplicht; ontbrekend/leeg stopt Compose |
| `WEB_PORT` | `8080` |
| `WEB_BIND_HOST` | `127.0.0.1` |

Web is de enige gepubliceerde service. `/api/*` gaat naar de interne Api; PostgreSQL en Api
publiceren geen hostpoorten. Alle drie containers zijn Linux-based. Database en gebruiker
heten `aigenterra`. Alleen PostgreSQL en Api ontvangen het wachtwoord; Api gebruikt Npgsql's
`PGPASSWORD` naast `ConnectionStrings__aigenterra`. De standaard databasegebruiker is de
door het PostgreSQL-image aangemaakte beheerder en wordt in v1 ook voor migrations gebruikt.

```powershell
docker compose ps
docker compose logs --tail 100 api
docker compose logs --tail 100 web
docker compose down
docker compose up -d --wait --wait-timeout 180
# Na gewijzigde broncode of een bewust bijgewerkte Dockerfile:
docker compose up -d --build --wait --wait-timeout 180
```

PostgreSQL bewaart executions en raw SourceEvidence in het projectgebonden volume
`aigenterra-selfhosted_postgres-data`, gescheiden van het Aspire-volume. Normale `down`/`up`
behoudt dit. **`down -v` verwijdert de data.** Een andere Compose-projectnaam selecteert
een ander volume. Verander een databasewachtwoord expliciet in PostgreSQL én `.env`;
een `.env`-wijziging wijzigt bestaande databasecredentials niet. Verwijder geen volume om
een wachtwoordfout op te lossen. Een persistent volume is geen backup; maak een databasebackup
voor upgrades met bestaande waardevolle opnames.

## Lokale recording-workflow (aanbevolen)

Gebruik een lokale PostgreSQL 17 Windows-service, onafhankelijk van Docker en AppHost.
Voer eenmalig vanuit PowerShell 7 als administrator onder je eigen Windows-account uit:

```powershell
pwsh -NoProfile -File scripts/Setup-LocalPostgres.ps1
```

Het script maakt een lege database, een vaste datamap en AppHost user secrets. Bestaande
Docker-data blijft intact. Zie [PostgreSQL, migrations en backups](docs/development/postgresql.md).
Daarna vanuit een normale terminal:

```powershell
pwsh -NoProfile -File scripts/Install-CodexHooks.ps1 -RepositoryPath 'C:\pad\naar\jouw\repo'
dotnet run --project src/Aigenterra.AppHost --launch-profile http
```

Vereist: Git, .NET uit `global.json`, PowerShell 7, Node 24 >=24.15, npm 11.17.0 en Codex met
hooks ingeschakeld in een vertrouwde repository. Start Codex in de opgenomen repo/worktree.
Open Web vanuit Aspire en gebruik Refresh om opnames te zien.

De Collector staat op een vaste gebruikerslocatie. Hooks bewaren bronbytes eerst in
`%LOCALAPPDATA%\Aigenterra\spool`; een door AppHost gestarte worker verzendt ze naar PostgreSQL.
API-uitval laat complete spoolrecords staan voor herlevering. Git/worktree start/end-context
wordt op het hookmoment gemeten. De hook blijft fail-open bij lokale opslagfouten.

```powershell
& "$env:LOCALAPPDATA\Aigenterra\collector\Aigenterra.Collector.exe" status
```

Voor de self-hosted stack start je dezelfde worker op de agentmachine met
`AIGENTERRA_INGEST_URL=http://127.0.0.1:8080/api/ingest/codex/hooks` en `drain --watch`.
Docker krijgt geen toegang tot je Git-worktrees of lokale Codex-runtime.

Zie [recording-hardening: worktrees, buffer en bewijsgrenzen](docs/development/recording-hardening.md)
voor de volledige workflow. App-server-capture blijft handmatig. De bestaande Aspire-containeroptie
blijft beschikbaar met `Database:Mode=Container` en `Parameters:postgres-password` in AppHost secrets.
De standaardmodus is External en vereist een expliciete connection string.

## Health, migrations en security

PostgreSQL moet TCP-ready zijn voordat Api start. Api past vóór serving alleen gereviewde
migrations toe; fouten stoppen startup en verschijnen in containerlogs. Compose zet
`Database__ApplyMigrationsOnStartup=true`. Zonder die instelling is automatisch migreren
alleen in Development actief. Er is geen schemareset of fallback naar in-memory.
Bij meerdere Api-instances, rolling deployments of gescheiden databaserechten wordt een
eenmalige migration-container/bundle de volgende stap.

`/api/alive` controleert procesrespons; `/api/health` controleert ook databaseconnectiviteit.
`/healthz` controleert Web. Health-antwoorden bevatten alleen statusinformatie. `depends_on`
ordent startup; latere unhealthy-statussen stoppen/herstarten afhankelijke containers niet.
`restart: unless-stopped` herstart beëindigde processen, ook bij herhaalde startupfouten.
Controleer bij problemen `ps` en logs; herstel de oorzaak zonder volumes te verwijderen.

> This deployment is intended for local or trusted private-network use. Do not expose it directly to the public internet without an authentication/TLS boundary.

LAN-toegang vereist een bewuste wijziging van `WEB_BIND_HOST`. Er is nog geen authentication,
authorization of automatische TLS. `.env` is een lokaal configuratiebestand, geen versleutelde
secret store. Docker-beheerders kunnen containerenvironment uitlezen; deel geen ongeredigeerde
`docker inspect`- of `docker compose config`-output. Gebruik `docker compose config --quiet`
voor validatie zonder secrets af te drukken. Runtime-secrets gaan niet de images in.

Builds gebruiken gepinde Linux-imageversies en het npm-lockfile. Versietags en OS-packages
kunnen door upstream worden herbouwd: dit garandeert geen bit-identieke builds. Updates zijn
bewuste onderhoudswijzigingen, zonder automatische update tooling.

## Verificatie

Voer de .NET- en Angular-checks uit zoals beschreven in de developmentdocumentatie.
De optionele smoketest vereist PowerShell 7 en Docker:

```powershell
pwsh -File scripts/Test-SelfHosted.ps1
```

De test maakt een unieke Compose-stack met eigen wachtwoord, hostpoort en testvolume,
controleert routing, ingest, migrations, health en dataretentie en verwijdert alleen eigen
resources. Er is geen interactie met het normale self-hosted of Aspire-volume.

Handmatige acceptatie: voer een echte Codex-turn uit, inspecteer Completed/status, agents en
timeline, bewaar de detail-URL, doe `down`/`up` zonder `-v` en open/refresh dezelfde detailroute.
Controleer dat secrets/evidence niet in Git, images of logs zijn terechtgekomen. Start daarna
Aspire en verifieer de bestaande developmentflow opnieuw.
