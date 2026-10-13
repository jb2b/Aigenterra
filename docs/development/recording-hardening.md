# Recording-hardening — fase 1

De minimale flight recorder bewaart ontvangen Codex-hooks duurzaam en neemt Git-context
op het hookmoment op. Automatische app-server-capture, transcriptarchivering en uitgebreide
correlatie/recovery horen niet bij deze fase.

## Starten

1. Configureer de [lokale PostgreSQL-service](postgresql.md).
2. Publiceer de Collector en installeer hooks per bestaande Git-repository:

   ```powershell
   pwsh -NoProfile -File scripts/Install-CodexHooks.ps1 -RepositoryPath 'C:\pad\naar\repository'
   ```

   De Collector staat daarna in `%LOCALAPPDATA%\Aigenterra\collector`. Dit script behoudt
   andere hooks en vervangt alleen de herkenbare Aigenterra-launchers. Herhalen is veilig.
   Na Collector-codewijzigingen voer je dit opnieuw uit om de gepubliceerde binary te verversen.

3. Start Aigenterra vanuit deze repository:

   ```powershell
   dotnet run --project src/Aigenterra.AppHost --launch-profile http
   ```

   AppHost start de API, Web en `Collector drain --watch`. PostgreSQL draait als aparte service.
   Open Web vanuit Aspire. De Collector-worker krijgt het API-endpoint van Aspire.

4. Start Codex in de opgenomen repo/worktree. Vertrouw de repo en review/vertrouw de
   Aigenterra-hooks via `/hooks` in de CLI. Nieuwe of gewijzigde hookdefinities worden anders
   overgeslagen, ook wanneer hooks ingeschakeld zijn. Dit geldt ook na vervanging van de oude
   launcher. Herstart reeds actieve Codex-sessies om gewijzigde hooks te laden. Zie de
   [officiële hookdocumentatie](https://learn.chatgpt.com/docs/hooks#review-and-trust-hooks).
5. Controleer een opname via de bestaande executionpagina en de lokale buffer:

   ```powershell
   & "$env:LOCALAPPDATA\Aigenterra\collector\Aigenterra.Collector.exe" status
   ```

Voor de toekomstige GTA Police-map: initialiseer eerst zelf de Git-repository, installeer dan
hooks, neem `.codex/hooks.json` op in de eerste commit en maak daarna worktrees. Elke worktree
gebruikt dezelfde Collector en spool. Reeds bestaande branches/worktrees zonder dat bestand
hebben de installatie of een commit met de hookconfiguratie nodig. Dit werk initialiseert
`Police Game` niet en wijzigt geen gebruikersbrede Codex-hooks.

## Self-hosted of losse worker

De lokale Collector heeft geen databasecredentials nodig. Start op de agentmachine:

```powershell
$env:AIGENTERRA_INGEST_URL = 'http://127.0.0.1:8080/api/ingest/codex/hooks'
& "$env:LOCALAPPDATA\Aigenterra\collector\Aigenterra.Collector.exe" drain --watch
```

De worker leidt `/ingest/codex/captures/v1` af van het bestaande ingestadres en behoudt `/api`.
`AIGENTERRA_CAPTURE_URL` kan het nieuwe endpoint expliciet instellen.
Zonder override is het endpoint `http://127.0.0.1:5248/ingest/codex/captures/v1`.
Gebruik `drain` zonder `--watch` voor één verzendronde. Draai geen workers met verschillende
bestemmingen tegen dezelfde spool: de bestemming geldt voor de hele buffer. Voor gescheiden
installaties geef je zowel hooks als worker dezelfde afzonderlijke `AIGENTERRA_SPOOL_PATH`.

## Wat wordt opgeslagen

- Elke ontvangen hook blijft byte-identiek beschikbaar in `raw_captures`, ook SessionStart,
  onbekende events, onbekende velden, ongeldige hook-JSON en niet-koppelbare events.
- Capture-ID, oorspronkelijke capturetijd, ontvangsttijd, hash, contextfase en Git-snapshot
  staan naast de bronbytes. Een database-trigger blokkeert UPDATE/DELETE van raw captures.
- `capture_outcomes` bevat de bestaande execution/observationkoppelingen of `archived_only`
  met een reden. Archivering en normalisatie zijn afzonderlijke stappen; mislukte normalisatie
  vernietigt geen brondata. Technische fouten vóór het opslaan van een outcome kunnen bij
  herlevering verdergaan. Er is geen algemene achtergrondcorrelatie of historische reconstructie.
- Bestaande observations behouden hun oorspronkelijke SourceEvidence, IDs, sequences en
  Stop/Interrupt-semantiek. Legacy-ingestendpoints en de handmatige commandresultaatcapture blijven.
- Model, permission mode, cwd, prompts, toolinput/-output en runtime-identiteiten blijven
  bewaard voor zover de hook ze levert. Er worden geen waarden afgeleid uit lokale config,
  commandoutput of claims van een agent. Geen nieuwe claim over tokengebruik of reasoning.
- De huidige Codex-adapter kan parents niet betrouwbaar reconstrueren en toolobservations
  blijven taskgebonden. Die beperking is zichtbaar in bestaande timelines en verandert hier niet.

Git wordt bij UserPromptSubmit en Stop/Interrupt op de agentmachine gelezen: primary
repositoryroot, gedeelde Git-directory, worktreepad, branch, HEAD en dirty-status, plus changed/
untracked paden. Geen bestandinhoud. Git-output is begrensd op 1 MiB per command en de hele
meting op 500 ms. Bij een timeout, ontbrekende Git of wijzigende HEAD is context onbekend;
`IsDirty=null` betekent nooit clean. Detached HEAD heeft een null branch; een lege repo een
null SHA. Bij ontbrekende cwd wordt geen procesdirectory gegokt. Git-status is een momentopname,
geen atomische filesystem-snapshot of bewijs dat alle veranderingen door deze run zijn gemaakt.

Het bestaande timeline-endpoint voegt nullable `startContext`/`endContext` toe; Web blijft
ongewijzigd. Het gebruikt de eerste gekoppelde snapshot per fase, geordend op capturetijd/ID.
Alle latere snapshots blijven raw beschikbaar. Oude recordings krijgen geen contextbackfill.
Tijden worden in PostgreSQL opgeslagen als UTC .NET ticks. Timelinequeries laden geen raw bytes.

## Buffer, aflevering en grenzen

`%LOCALAPPDATA%\Aigenterra\spool` staat buiten Git en buiten tijdelijke directories:

- `staging`: nog niet volledig afgeronde lokale writes; na een proceskill handmatig onderzoeken.
- `pending`: complete records die op een databasebevestiging wachten.
- `retained`: oversized payloads of permanent afgewezen envelopes; bytes blijven behouden.
- `acknowledged`: de API bevestigde duurzame opslag; overgebleven directories wijzen op
  onderbroken lokale cleanup en zijn geen ontbrekende databaseopnames.

De hook doet alleen lokale capture, retourneert `{}`/exitcode 0 en blijft fail-open.
De worker verstuurt sequentieel met een exclusieve spool-lock. Na transportfouten volgt back-off
van 2 tot maximaal 30 seconden. Een verloren HTTP-antwoord kan dezelfde capture opnieuw
versturen; dezelfde ID met andere inhoud geeft conflict. De worker verwijdert lokale evidence
pas na `archived=true` voor de juiste capture-ID.

De bronlimiet is standaard 16 MiB (`AIGENTERRA_CAPTURE_MAX_BYTES` bij de Collector;
`Capture:MaxPayloadBytes` bij de API). Oversized bytes worden lokaal bewaard. De bestaande
legacy-endpoints blijven 1 MiB. De standaard proxygrens van 30 MiB past bij de nieuwe
envelope; verhoog die afzonderlijk als je bewust grotere captures configureert.

`status` toont pending/retained/incomplete/acknowledged aantallen en lokale foutredenen.
Normalisatiefouten na succesvolle archivering vind je via databasegereedschap:

```sql
SELECT disposition, reason, count(*) FROM capture_outcomes GROUP BY disposition, reason;
```

Er is geen automatische retentie of cleanup van historische raw evidence. Houd schijfruimte
in de gaten. Fail-open betekent dat een volle schijf, ontbrekende binary, uitgeschakelde hook,
proceskill tijdens capture of een event dat Codex nooit levert alsnog een gat kan veroorzaken.
Een schijfbuffer beschermt tegen tijdelijke API-uitval; hij is geen backup en garandeert geen
volledige brondekking. Start/end-tijden zijn hook-capturetijden, geen nieuw bewezen runtime-duration.

## Verificatie

De tests gebruiken tijdelijke Git-repositories en echte worktrees; PostgreSQL-tests maken
geïsoleerde databases. `RecordingSmokeTests` start echte API/Collector-processen, laat de API
uitstaan tijdens opname, verzendt na herstel, controleert beide worktrees inclusief gewijzigde
HEAD, bytebehoud en herstartretentie. De bronpayloads zijn deterministische hookfixtures;
deze test verbruikt geen modelgebruik.

```powershell
dotnet test tests/Aigenterra.Infrastructure.Tests -c Release --filter FullyQualifiedName~RecordingSmokeTests
```

Gebruik voor een echte Codex-acceptatie dezelfde workflow in een tijdelijke vertrouwde repo,
voer één read-only toolcall uit en controleer Completed, vier observations en start/end-context.
Controleer voor GTA Police altijd één echte opname voordat serieuze ontwikkeling begint.

### Uitgevoerde verificatie op 16 september 2026

- Solution restore, Release-build en format-check geslaagd; **151 tests geslaagd**, nul overgeslagen.
  Ook Debug bouwt zonder waarschuwingen. PostgreSQL 17.11 draaide hiervoor als geïsoleerde
  tijdelijke testserver; bestaande Docker-databases zijn niet gebruikt of gewijzigd.
- De proces-smoketest bewaart acht hooks uit twee worktrees tijdens API-uitval, levert ze na
  herstel byte-identiek af en leest dezelfde timeline na API-herstart. Start/end-SHA, branches,
  dirty-status en de gedeelde Git-directory zijn gecontroleerd.
- Nieuwe en bestaande databases migreren; oude recordings blijven leesbaar. Verloren
  acknowledgements, herstart van de worker, parallelle writers, oversized records en fouten
  tijdens normalisatie zijn getest. Herinstallatie behoudt andere hooks zonder duplicaten.
- De daadwerkelijk geïnstalleerde Windows-launcher bewaart UTF-8/CRLF byte-identiek.
- Een echte Codex CLI-turn (0.153.4) eindigde succesvol, maar leverde in de nieuwe testrepo
  geen hooks op. De hooktrust-stap is nog nodig; dit telt niet als geslaagde runtime-opname.
- De eerste administratorstart en aanvullende AppHost-proef werden door automatische
  goedkeuringscontrole geblokkeerd. Na expliciete toestemming van de gebruiker is het
  setupscript alsnog succesvol uitgevoerd: PostgreSQL 17.11 draait als automatische service,
  met database/owner `aigenterra`, localhostpoort 5432 en de afgesproken ProgramData-datamap.
  AppHost-secrets zijn ingesteld. AppHost is gestart; `/health` geeft 200, alle vijf migrations
  zijn toegepast en de worker levert de bestaande lokale buffer af. Er zijn ruim negentig raw
  captures opgeslagen; een echte opgenomen timeline is leesbaar met startcontext. Events zonder
  bestaande correlatie blijven als `archived_only` bewaard, zoals bedoeld.

De opnameketen is bewezen met echte API/Collector-processen en deterministische hooks; de lokale
service en AppHost draaien ook met echte captures. Voor een nieuwe GTA Police-repository blijven
hookinstallatie, review/trust en een controleopname nodig. Bestaande actieve Codex-sessies kunnen
nog de eerder geladen launcher gebruiken: controleer `/hooks` ook na een sessieherstart.
Extra telemetrie of app-server-automatisering is geen voorwaarde.

### Windows-launcher na acceptatiediagnose

De hookinstaller vereist PowerShell 7 (`pwsh.exe` op PATH). Hij installeert een
launcher naast de Collector en gebruikt `-File`; een geneste dubbel gequote
`-Command` kan door de buitenste PowerShell worden vervormd voordat capture start.
Voer na installatie onderstaande regressieproef uit:

```powershell
./scripts/Test-CodexHookLauncher.ps1 -RepositoryPath C:\pad\naar\repository
```

Na een gewijzigde hookdefinitie moet die definitie opnieuw worden gereviewd en
vertrouwd in Codex. Bestaande sessies kunnen oude hooks vasthouden. De installer
wijzigt geen trustinstellingen en geen execution policy. De lokale launcher gebruikt
het vaste gebruikerspad; installeer hooks opnieuw wanneer de gebruikerslocatie verandert.
Zie `acceptance-2026-09-16.md` voor de echte twee-worktreeproef na deze reparatie.
