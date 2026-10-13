# Herhaling na diagnose — 16 september 2026

De eerste twee pogingen hieronder blijven als historische bevindingen bewaard.
De derde poging gebruikt echte Codex CLI 0.153.4-runs en de herstelde Windows-launcher.

## Oorzaak en herstel 

De geïnstalleerde `commandWindows` startte een tweede PowerShell met een dubbel
gequote `-Command`. De buitenste PowerShell vulde `$p` en `$env:LOCALAPPDATA` al in.
Daardoor bevatte het binnenste commando onder andere `&` zonder operand en faalde
het parsen voordat de Collector werd gestart. Dit is onafhankelijk gereproduceerd:
exitcode 1, geen spoolrecord. Vertrouwde hooks konden hierdoor toch niets opnemen.

De installer plaatst nu `invoke-aigenterra-collector.ps1` naast de gepubliceerde
Collector en gebruikt `pwsh.exe -NoProfile -File` met het volledige lokale pad.
PowerShell 7 is een vereiste; de Windows PowerShell 5.1-test met `-File` werd door
de lokale execution policy geweigerd. Er is geen execution policy, sandbox of
hooktrust-beveiliging uitgeschakeld. Alleen de goedgekeurde testhooks zijn opnieuw
vertrouwd na controle van de gewijzigde launcher.

`scripts/Test-CodexHookLauncher.ps1` voert de echte launcher via een buitenste
PowerShell uit en verifieert exitcode, `{}`, één duurzaam record en exact behoud
van UTF-8/CRLF-invoer. Deze regressieproef slaagt. Herinstallatie levert acht events
met elk één Aigenterra-launcher op.

De losse Git-fout is geen recorderfout: `workspace-write` weigerde een `index.lock`
in de gedeelde Git-directory, ook met extra writable roots. Deze proef wijzigt
bestanden zonder commit; de sandbox blijft actief. Er wordt dus **geen geslaagde
Codex-commit of gewijzigde eind-HEAD** geclaimd.

## Resultaten van de derde poging

| Controle | World | Vehicle |
|---|---|---|
| Branch | `test-world-fixed` | `test-vehicle-fixed` |
| TaskExecution | `01a0ab0f-4320-7b72-bcf9-c6ea1b1e9278` | `01a0ab0f-43a6-7b74-b737-e6bf5e0fad6b` |
| Session | `01a0ab0f-2af2-7bc0-a525-c6c311b45a5f` | `01a0ab0f-2c4f-7810-b2d3-9c30db6f3518` |
| Status | completed | completed |
| Raw captures | 9 | 9 |
| Observations | 8 | 8 |
| Nieuw ongecommit bestand | world.txt | vehicle.txt |
| Volledige promptinhoud | gecontroleerd | gecontroleerd |
| Worktree/branch/start- en eind-SHA | gecontroleerd | gecontroleerd |

Beide HEADs blijven `5bce4b6f244bd770f0ba395ae693036dbc55ebfc`. De startcontext toont
het gewijzigde hookconfiguratiebestand; de eindcontext daarnaast het nieuwe untracked
bestand. De volledige promptinhoud klopt; de npm/PowerShell-CLI-wrapper zet LF om
naar CRLF en voegt een afsluitende newline toe. De bestaande normalisatie trimt
buitenste whitespace van de execution objective; de ontvangen raw payload blijft intact.

Tijdens API-uitval maakten beide echte runs hun bestand. Acht records bleven lokaal
pending. Een nieuw gestart drainproces kreeg `delivery_failed` en verwijderde niets.
Na AppHost/worker-herstart werden ze afgeleverd terwijl de turns nog actief waren.
Alle acht capture-ID's komen exact één keer in PostgreSQL voor en hun payloads zijn
byte-identiek aan de apart bewaarde spoolbestanden. Daarna eindigden beide turns
met opgenomen Stop-events. Spool: pending 0, retained 0, incomplete 0, geen failures.

Solution restore, Debug-build, format-check en **151 tests** slagen. De eerste
regressieruns hadden geen bruikbare testdatabase (Docker niet beschikbaar; daarna
ontbrekende tijdelijke serverconfiguratie). Met de bestaande geïsoleerde PostgreSQL
17.11-testserver en zijn lokale credentials slagen alle 35 infrastructuurtests.
Frontendbestanden zijn in deze diagnose niet gewijzigd.

Bewijs: `attempt-3` onder de hieronder vermelde tijdelijke acceptatiedirectory,
met runtime-JSONL, prompts, timestamps, outage-spool, database-export en verificatie-JSON.

De nieuwe PostgreSQL-serviceherstart is niet uitgevoerd: Windows retourneerde dat de UAC-start door de gebruiker werd geannuleerd. AppHost/API/worker zijn wel opnieuw gestart. De opname-/uitvalcriteria slagen; het criterium PostgreSQL-herstart met deze nieuwe opnames blijft open. De eerdere geslaagde databaseherstart hieronder geldt alleen voor de toen bestaande data.

---
# Praktische acceptatie — echte Codex-runs in worktrees

Datum: 16 september 2026. Uitgevoerd op Windows met Codex CLI 0.153.4 en de lokale
PostgreSQL 17.11-service. Dit rapport beschrijft een praktische proef, niet de eerdere
geslaagde tests met deterministische hookfixtures.

## Oordeel

**Niet geslaagd / geen GO voor de geteste CLI-worktree-route.**

Echte Codex-processen hebben tijdens API-uitval bestanden gemaakt in twee worktrees,
maar hun hooks zijn niet teruggevonden in de lokale spool of in PostgreSQL. Daardoor
ontbreken de twee verwachte TaskExecutions en alle bijbehorende recordercontroles.
Daarnaast blokkeerde de Windows-sandbox het committen via de gedeelde Git-directory.
De precieze oorzaak van de ontbrekende hookcaptures is niet vastgesteld.

De bestaande database heeft een volledige serviceherstart wel zonder aantoonbare
gegevenswijziging doorstaan. Dit resultaat bewijst geen succesvolle opname van de nieuwe runs.

## Opzet en uitvoering

- Tijdelijke hoofdrepository met gecommitteerde Aigenterra-hookconfiguratie en twee worktrees.
- De bestaande, gepubliceerde Collector is gebruikt; geen applicatiecode aangepast.
- Na expliciete toestemming van de gebruiker is hooktrust vastgelegd. `hooks/list`
  rapporteerde voor beide oorspronkelijke worktrees acht enabled/trusted Collector-hooks,
  geladen uit de gedeelde hoofdrepository. De launcher is vergeleken met het installatiescript.
- Twee echte `codex exec --json`-processen kregen een eigen volledige prompt. Opdracht:
  wachten op een controller, een klein tekstbestand maken, committen en op herstel wachten.
- Eerste poging: `workspace-write`, met de tijdelijke testdirectory als extra writable root.
  Beide runs maakten een bestand, maar Git weigerde schrijven van `index.lock`.
- Herhaling in twee nieuwe worktrees: ook de gedeelde `.git` en de Aigenterra-spool expliciet
  als writable roots meegegeven. De sandbox bleef actief; ook deze poging kon niet committen.
- Tijdens de herhaling is AppHost met API en worker gestopt. HTTP-onbereikbaarheid is gecontroleerd.
- Beide bestanden zijn aantoonbaar na het begin van de API-uitval geschreven.
- Na de blokkade is PostgreSQL volledig herstart en AppHost opnieuw gestart.

De gevraagde volledige volgorde kon niet worden voltooid: de agents beëindigden hun turn
na de commitblokkade, vóór het tweede wachtpunt. API-herstel vond daardoor niet tijdens
een nog actieve testturn plaats. Dit is geen vervanging voor een geslaagde volledige proef.

## Bewijs uit de tweede poging

| Gegeven | World | Vehicle |
|---|---|---|
| Branch | `test-world-retry` | `test-vehicle-retry` |
| Codex-session | `01a0ab04-1232-7ef3-b245-ef8f5fc85906` | `01a0ab04-137a-7f61-a87e-fc12d1cdc142` |
| Bestand | `world.txt` | `vehicle.txt` |
| Geschreven, UTC | 16:20:26.746 | 16:20:26.744 |
| CLI beëindigd, UTC | 16:20:45.577 | 16:21:01.377 |
| CLI exitcode | 0 | 0 |
| Git-resultaat | Geen commit; bestand untracked | Geen commit; bestand untracked |

De API was vanaf **16:20:19.896 UTC** aantoonbaar onbereikbaar. Beide worktrees startten
op `5bce4b6f244bd770f0ba395ae693036dbc55ebfc`; HEAD bleef onveranderd. Een CLI-exitcode
0 betekende hier dat Codex zijn antwoord afrondde, niet dat alle opgedragen stappen slaagden.

Ook voor de twee eerste sessions (`01a0aaff-59f1-7591-93bd-e05dac82d7b3` en
`01a0aaff-5aaf-7091-bec8-45fdbf3834bb`) zijn geen raw captures gevonden.
De databasecontrole op alle vier session-ID's leverde **0** records op.

## Acceptatiecriteria

| Controle | Resultaat |
|---|---|
| Twee afzonderlijke TaskExecutions | **Niet gehaald:** geen nieuwe testexecutions opgeslagen. |
| Correcte opgenomen worktreepaden | **Niet aantoonbaar:** geen captures/timelines voor deze runs. |
| Verschillende opgenomen branches | Branches bestaan in Git, maar zijn niet opgenomen. |
| Correcte opgenomen start-HEAD | HEAD onafhankelijk bekend; geen recorder-snapshot. |
| Correcte opgenomen eind-HEAD | HEAD onveranderd; geen recorder-snapshot. |
| Volledige prompts in Aigenterra | **Niet gehaald:** testprompts ontbreken. |
| Toolobservations | **Niet gehaald:** ontbreken voor de testruns. |
| Raw evidence | **Niet gehaald:** nul captures voor alle vier test-session-ID's. |
| Spool uiteindelijk leeg | Ja, maar ook tijdens de proef leeg; **geen bewijs van aflevering**. |
| Nieuwe testdata na volledige herstart | Niet toetsbaar: nieuwe testdata ontbreekt. |
| Bestaande data na volledige herstart | **Geslaagd**, zie hieronder. |

## PostgreSQL-herstart en bestaande historie

De PostgreSQL-service is op **16:22:21 UTC** opnieuw gestart. Serviceproces veranderde
van PID 28216 naar PID 33716. AppHost/API/worker zijn eveneens opnieuw gestart.

Vóór en na de databaseherstart zijn aantallen en deterministisch gesorteerde inhoudsfingerprints
van alle records in deze tabellen vergeleken:

| Tabel | Aantal vóór/na | MD5-vergelijkingsfingerprint vóór én na |
|---|---:|---|
| `raw_captures` | 105 / 105 | `350cd5b754362991d0558a2810e82ed8` |
| `task_executions` | 1 / 1 | `5a5bfcd326095a0549507d4d4241fa48` |
| `observations` | 34 / 34 | `c11407e5b0d7b63cfda85b98d5931068` |

Deze fingerprints dienen uitsluitend voor de vóór/na-vergelijking. De raw-vergelijking
omvat ook payloadbytes en context. Na herstel gaf `/health` HTTP 200 en was de bestaande
timeline met 34 observations via de API leesbaar. Spoolstatus: pending 0, retained 0,
incomplete 0, acknowledged 0, geen geregistreerde failures.

## Bewijsbestanden en vervolg

Tijdelijke fixture en uitvoer staan lokaal onder:

`C:\Users\jeroe\AppData\Local\Temp\aigenterra-acceptance-c572cc1be4e549b1b63726a9811f5d87`

Daar staan de volledige testprompts, CLI-JSON-uitvoer, stderr, markerbestanden, vastgelegde
hookhashes, outage-tijd en databasevergelijkingen. `attempt-2` bevat de herhaling. De uitvoer
is testmateriaal en geen automatisch door Aigenterra gearchiveerd transcript.

Vervolg: eerst vaststellen waarom de CLI-route ondanks de gecontroleerde hookconfiguratie
geen Collector-records oplevert. Los daarnaast de Git-schrijfbeperking van de testopzet op,
of kies expliciet een proef zonder commits. Herhaal daarna dezelfde volledige acceptatie;
gebruik de eerdere fixturetests of de lege spool niet als vervangend bewijs.

`Police Game`, bestaande opnames en applicatiecode zijn niet gewijzigd. Geen commit, push
of milestone-tag gemaakt. PostgreSQL en Aigenterra zijn weer draaiend achtergelaten.

Hercontrole na de laatste AppHost-herstart: alle 123 raw captures en drie volledige timelines ongewijzigd; alle 16 evidence-downloads van beide nieuwe executions geven HTTP 200 met inhoud. De nieuwe launcher is ook in de Aigenterra-repository geïnstalleerd; de gewijzigde hoofdrepository-hooks moeten door de gebruiker opnieuw worden vertrouwd (alleen testhooktrust was expliciet toegestaan).
