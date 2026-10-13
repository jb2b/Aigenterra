# PostgreSQL lokaal gebruiken

De API gebruikt altijd PostgreSQL; Domain en Application kennen geen EF Core, Npgsql of Aspire.
De standaard AppHost-modus is nu **External**. De database blijft onafhankelijk van AppHost draaien.

## Eenmalig: Windows-service

Open **PowerShell 7 als administrator onder dezelfde Windows-gebruiker** die AppHost gebruikt.
Vanuit de repositoryroot:

```powershell
pwsh -NoProfile -File scripts/Setup-LocalPostgres.ps1
```

Het script downloadt PostgreSQL 17.11-3 van EDB en controleert de gepinde SHA-256.
Het installeert alleen server en commandlinetools, maakt service `postgresql-aigenterra-17`
met automatisch starten, database/owner `aigenterra` en localhostpoort 5432.
Binaries staan in `C:\Program Files\PostgreSQL\17`; de blijvende data staat in
`C:\ProgramData\Aigenterra\PostgreSQL\17\data`.
Er wordt geen bestaande installatie, database of datamap overschreven of verwijderd.
Bestaande Docker-data wordt niet geïmporteerd.

Wachtwoorden worden willekeurig gegenereerd, niet afgedrukt en als Windows-DPAPI-beveiligde
credentials bewaard in `%LOCALAPPDATA%\Aigenterra\setup`. Die map is beperkt tot de gebruiker,
SYSTEM en administrators. De tijdelijke installer-optiesfile wordt na installatie verwijderd.
AppHost krijgt `ConnectionStrings:aigenterra` en `Database:Mode=External` via user secrets.
User secrets zijn een lokale ontwikkelvoorziening, geen versleutelde vault.
Verlies of wijziging van credentials is nooit reden om de datamap te verwijderen.

Eenmalige servicehandelingen vanuit een administrator-terminal:

```powershell
Get-Service postgresql-aigenterra-17
Start-Service postgresql-aigenterra-17
# Alleen indien je PostgreSQL bewust wilt herstarten:
Restart-Service postgresql-aigenterra-17
```

De service start daarna met Windows. Een normale AppHost-herstart raakt PostgreSQL niet.

## Bestaande externe PostgreSQL gebruiken

Configureer in AppHost user secrets `ConnectionStrings:aigenterra` met host, port, database,
username en het lokale wachtwoord. Ook `ConnectionStrings__aigenterra` in de procesomgeving
wordt ondersteund. Stel `Database:Mode=External` in; een ontbrekende connection string geeft
bij startup een fout, zonder fallback. Gebruik een eigen database-owner met LOGIN voor
Aigenterra; een PostgreSQL-superuser is voor de API niet nodig.

```powershell
dotnet run --project src/Aigenterra.AppHost --launch-profile http
```

Controleer `/health` op de API. AppHost start API, Web en de lokale Collector-verzendworker.
Als PostgreSQL uitstaat kan API-startup falen; start PostgreSQL en herstart AppHost.
Hooks kunnen ondertussen naar hun lokale spool blijven schrijven.

## Migrations en databasegereedschap

De bestaande vier migrations blijven behouden. `20260916150255_RecordingHardening` voegt
alleen `raw_captures` en `capture_outcomes` toe, inclusief een trigger die UPDATE/DELETE van
raw captures afwijst. Oude executions en evidence blijven ongewijzigd; nieuwe context is nullable.
Startup gebruikt de expliciete allowlist. Onbekende pending migrations stoppen startup.
`Database:ApplyMigrationsOnStartup` is standaard aan in Development; Compose zet deze expliciet aan.

```powershell
dotnet tool restore
dotnet ef migrations script --idempotent --project src/Aigenterra.Infrastructure --startup-project src/Aigenterra.Infrastructure --context ExecutionDbContext --output artifacts/execution-migrations.sql
```

Maak `artifacts` zo nodig eerst aan. De design-time factory leest een connection string uit de
procesomgeving, niet uit AppHost secrets. Met de door het setupscript aangemaakte credentials:

```powershell
$dbCredential = Import-Clixml "$env:LOCALAPPDATA\Aigenterra\setup\aigenterra-credential.xml"
$env:ConnectionStrings__aigenterra = 'Host=127.0.0.1;Port=5432;Database=aigenterra;Username=aigenterra;Password=' + $dbCredential.GetNetworkCredential().Password
try {
    dotnet ef database update --project src/Aigenterra.Infrastructure --startup-project src/Aigenterra.Infrastructure --context ExecutionDbContext
} finally {
    Remove-Item Env:ConnectionStrings__aigenterra
}
```

Handmatige inspectie vraagt interactief het wachtwoord; druk credentials niet af in logs:

```powershell
& 'C:\Program Files\PostgreSQL\17\bin\psql.exe' -h 127.0.0.1 -U postgres -d aigenterra -W -c 'SHOW data_directory;'
```

Voor backup met het lokaal opgeslagen applicatiewachtwoord (de doelmap moet bestaan):

```powershell
$dbCredential = Import-Clixml "$env:LOCALAPPDATA\Aigenterra\setup\aigenterra-credential.xml"
$env:PGPASSWORD = $dbCredential.GetNetworkCredential().Password
try {
    & 'C:\Program Files\PostgreSQL\17\bin\pg_dump.exe' -h 127.0.0.1 -U aigenterra -d aigenterra -Fc -f 'D:\Backups\aigenterra.dump'
} finally {
    Remove-Item Env:PGPASSWORD
}
```

Bewaar backups buiten de datamap. Test herstel in een aparte lege database met `pg_restore
--no-owner --no-privileges --exit-on-error`; overschrijf niet de actieve recorderdatabase.
Maak een backup vóór toekomstige upgrades. Geen `EnsureCreated`, `EnsureDeleted` of schemareset.

## Containeroptie behouden

```powershell
dotnet user-secrets set 'Database:Mode' 'Container' --project src/Aigenterra.AppHost
dotnet user-secrets set 'Parameters:postgres-password' '<eigen-lokaal-wachtwoord>' --project src/Aigenterra.AppHost
dotnet run --project src/Aigenterra.AppHost --launch-profile http
```

Aspire gebruikt dan het bestaande volume `aigenterra-postgres-data`. Compose gebruikt het
aparte volume `aigenterra-selfhosted_postgres-data`. Normale restarts behouden beide;
`docker compose down -v` verwijdert Compose-data. External en Container gebruiken verschillende
databases: omschakelen importeert geen data. Zet voor de lokale service de modus terug op External.

## Verificatie

```powershell
dotnet restore Aigenterra.slnx
dotnet build Aigenterra.slnx -c Release --no-restore
dotnet format Aigenterra.slnx --verify-no-changes --no-restore
dotnet test Aigenterra.slnx -c Release --no-build --no-restore
```

Database-tests gebruiken standaard Testcontainers. Zonder Docker kan
`AIGENTERRA_TEST_POSTGRES_CONNECTION` naar een **testserver** wijzen met CREATEDB-rechten.
De fixture maakt uitsluitend unieke `test_<guid>` databases en verwijdert alleen die databases.
Dit is geen automatische fallback; zonder expliciete testverbinding blijft Docker vereist.

Zie [recording-hardening](recording-hardening.md) voor de lokale spool en Git/worktree-capture.
