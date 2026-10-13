# Deterministic Evaluation

## Evaluator v2: poging versus onvoldoende evidence

`dotnet-command-results` v2 gebruikt dezelfde strikte commandherkenning als v1.
Directe build/test-hooks bewijzen een opgenomen commandrequest of toolactiviteit;
zonder betrouwbare terminale metadata blijft de uitkomst onbekend. Alleen een
prehook bewijst niet dat het commando daadwerkelijk is uitgevoerd. Build en Tests
blijven afzonderlijke findings, met alle pogingen en hun evidenceverwijzingen.

De UI onderscheidt **Observed — outcome unknown** van **Unknown — insufficient
evidence**. Success/failure vereist nog steeds expliciete terminale metadata en
is geen claim dat testcases draaiden of slaagden. Gemengde pogingen behouden hun
afzonderlijke resultaten. Scripts, wrappers en samengestelde commando's worden
niet uitgepakt en stdout wordt niet gebruikt om een exitcode te raden.

Zonder gedecodeerde command evidence geeft v2 Unknown, ook als alleen prompt- of
lifecycle-observations bestaan. NotObserved vereist niet-lege, interpreteerbare
command evidence zonder herkende poging voor de betreffende check. Het betekent
uitsluitend afwezigheid in dat materiaal; ontbrekende hooks en ongekoppelde raw
captures kunnen niet worden uitgesloten.

Er is geen schema- of API-wijziging. Opgeslagen v1-snapshots blijven ongewijzigd
leesbaar, inclusief hun oorspronkelijke conclusies. Expliciet Re-evaluate maakt
een v2-snapshot naast v1; dezelfde versie en watermark zijn idempotent. Er vindt
geen automatische re-evaluation plaats. De onderstaande handmatige capture van
terminale metadata is optioneel en wordt niet geautomatiseerd: de reeds ontvangen
hooks kunnen zonder die stap worden geëvalueerd.

Een Evaluation is een immutable afleiding uit opgenomen evidence voor één
TaskExecution, onafhankelijk van Experiments. Ze wijzigt geen observation of
executionstatus. Build en Tests zijn checks; findings bevatten afzonderlijke pogingen.

## Gebruik

1. Bouw de Windows-Collector in Debug en start Codex met het ingestadres in zijn
   environment, zoals beschreven in de README. De bestaande hooks blijven gelden.
2. Voer afzonderlijke directe `dotnet build`- en/of `dotnet test`-commands uit.
3. Capture na afloop de terminale metadata van de betreffende turn:

   ```powershell
   pwsh -NoProfile -File scripts/Capture-CodexCommandResults.ps1 `
     -SessionId '<Codex session ID>' -TurnId '<Codex turn ID>' `
     -IngestUrl 'http://127.0.0.1:8080/api/ingest/codex/hooks'
   ```

   SessionId is de Codex-session-ID, niet Aigenterra's ConversationId. TurnId staat
   bij de runtime reference van de execution. Voor Aspire is het ingestadres
   `http://127.0.0.1:5248/ingest/codex/hooks`. Het script gebruikt anders
   `AIGENTERRA_INGEST_URL`, met behoud van een eventueel `/api`-prefix.
4. Open de execution, klik Evaluate en inspecteer Build en Tests. Evidenceknoppen
   maken observations zichtbaar, ook voorbij de eerste 100 items.
5. Na late observations: Refresh en Re-evaluate. Eerdere evaluaties blijven bestaan.

Capture vereist lokaal PowerShell 7 en de geïnstalleerde, aangemelde Codex CLI.
De centrale Linux-stack heeft geen Codex-runtime of runtimecredentials nodig.
Capture is expliciet: geen Worker, pollingservice of automatische Stop-hook.
Mislukken verandert of blokkeert de geobserveerde turn niet; het operatorcommand
geeft wel exitcode 1 met een algemene foutmelding. Reeds verzonden records blijven
behouden; opnieuw uitvoeren kan de ontbrekende records aanvullen.

## Aanvullende evidencebron

Codex CLI 0.153.4 is met echte opnames geverifieerd. Hooks bevatten commandtekst,
maar geen exitcode. `codex app-server` leest dezelfde bestaande sessie via
`thread/read`, zonder haar te hervatten. `commandExecution.id` komt overeen met
`tool_use_id`; terminale `status` en `exitCode` leveren de ontbrekende metadata.
Zie het [officiële read-contract](https://learn.chatgpt.com/docs/app-server#read-a-stored-thread-without-resuming).

Het script start een kortlevend lokaal stdio-app-serverproces, initialiseert het
protocol en leest alleen de opgegeven sessie. Daarna selecteert het de opgegeven
turn. Geen `thread/start`, `thread/resume`, `turn/start` of `command/exec`, geen
modelaanroep en geen transcriptbestand wordt gevolgd. De read heeft een deadline
van 20 seconden en een limiet van 16 MiB aan ontvangen tekens vóór interpretatie.
De volledige sessiehistorie kan tijdelijk in lokaal geheugen worden geladen;
ze wordt niet opgeslagen of verstuurd.

Alleen terminale commanditems worden geprojecteerd: source-contractversie,
runtimeversie, sessie, turn, item-ID, status, exitcode en commandActions. Output,
prompts, berichten en cwd worden niet verstuurd. HTTP heeft een timeout van 500 ms
per record en een limiet van 1 MiB. Geen queue of automatische retry.
`-InspectOnly` toont deze beperkte projecties lokaal zonder HTTP-ingest.

De grens is `POST /ingest/codex/command-results` (via Web met `/api`-prefix).
De API controleert dat sessie en turn bij dezelfde bestaande conversation horen.
De adapter slaat de projectie op als afzonderlijke ToolResult met SourceEvidence.
Originele hookbytes blijven ongewijzigd; capture mag na Completed plaatsvinden.
Identieke replay is een no-op. Conflicterende records blijven behouden en leiden
tot Unknown. Dit is geen cryptografische attestatie: wie binnen de bestaande
vertrouwde ingestboundary mag schrijven, kan evidence aanleveren. De local/private
securitygrens en noodzaak van auth/TLS bij publicatie blijven gelden.

## Betekenis en beperkingen

- RequestedOnly: aanvraag opgenomen; uitvoering is niet bewezen.
- Succeeded / Failed: terminale runtime-exitcode 0 / niet-nul.
- Unknown: resultaat ontbreekt, is niet interpreteerbaar of conflicteert.
- NotObserved: geen herkende poging in ondersteunde evidence, niet 'nooit uitgevoerd'.
- Een succesvol testcommand bewijst geen uitgevoerde of geslaagde testcases.
- Alleen één eenvoudige rechtstreekse `dotnet build`/`dotnet test` is ondersteund,
  inclusief letterlijke gequote projectpaden. Help, discovery, response files,
  custom targets, scripts, shellketens, pipelines, redirects en expansies vallen af.
- Het opgeloste executable, shellprofiel en projectinhoud worden niet geattesteerd.
- In de geverifieerde runtime bevat één commandActions-item van type `unknown`
  het oorspronkelijke command. Andere actionvormen worden niet uitgepakt of gegokt.
- Timestamps/sequence bepalen een stabiele presentatie, geen bewezen bronvolgorde.
  Er is geen algemene 'final passed'-boolean; eerdere mislukkingen blijven zichtbaar.
- Collector-uitval kan ontbrekende pogingen veroorzaken. Capture verzint geen
  execution wanneer de vereiste session/turn-hooks ontbreken.

## Opslag en API

Een consistente PostgreSQL-inputsnapshot levert de watermark: hoogste opgenomen
observationsequence. Evaluation bewaart evaluator `dotnet-command-results`, versie 1
en categorie Deterministic. Gewijzigde decoding of regels vereisen een nieuwe versie.
Findings bewaren verklaringen en ObservationIds, geen command- of payloadkopieën.

Tabellen: `evaluations`, `evaluation_findings`, `evaluation_attempts`,
`evaluation_evidence`. Composite foreign keys bewaken dezelfde execution en
restrict-delete. Execution/evaluator/versie/watermark is uniek; ook gelijktijdige
aanvragen delen hetzelfde volledige resultaat. Eén gereviewde migration voegt
dit toe zonder bestaande evidence te wijzigen.

- `POST /task-executions/{id}/evaluations`: synchroon, geen requestbody; 201 met
  relatieve Location voor nieuw resultaat, 200 voor dezelfde snapshot.
- `GET /task-executions/{id}/evaluations`: maximaal 20 metadata-items, nieuwste
  UUIDv7-identiteit eerst, met opaque `nextCursor` voor keysetpaginering.
- `GET /task-executions/{id}/evaluations/{evaluationId}`: volledig resultaat met
  `hasNewerEvidence`. Reads evalueren of wijzigen niets.

Onbekende resources geven 404; ongeldige cursor/lege ID 400 Problem Details.
Technische failures leveren geen gedeeltelijke Evaluation op. Raw SourceEvidence
blijft buiten de browsercontracten.

## Verificatie

`scripts/Test-SelfHosted.ps1` controleert ook capture-ingest, Unknown met hooks-only,
resultaatbewijs, history, idempotentie, evidence-referenties, validatie en behoud
na down/up. Met `-RealCodex` voert het aanvullend een echte acceptatieturn uit in
de afzonderlijke teststack en leest de runtime-resultaten. Dit opt-in testpad
gebruikt de bestaande Codex-accountconfiguratie en verbruikt modelgebruik; de
productiecapture zelf niet. Alleen eigen testcontainers en testvolumes worden verwijderd.

### Uitgevoerde acceptatie — 9 september 2026

- Solution restore, Release-build, format-check en 133 .NET-tests geslaagd;
  Angular production-build, 15 tests en Prettier-check geslaagd. Release vermijdt
  de Debug-binaries die de bestaande VS Code/Aspire-processen in gebruik houden.
- Linux Compose-smoke geslaagd, inclusief de nieuwe migration, API-contracten,
  snapshothergebruik, late evidence, referenties en behoud na down/up. De bestaande
  checks op netwerkpublicatie, gezondheid, database-uitval en Api-vervanging slagen ook.
- `-RealCodex` geslaagd met Codex CLI 0.153.4 en ongewijzigde Windows-hooks:
  afzonderlijke build- en testcommands met exitcode 0 en 1. De niet-nul-commands
  gebruiken bewust een ontbrekend project: dit bewijst een mislukt command, geen
  falende testcase. Alle vier pogingen zijn via expliciete tool-ID's gekoppeld aan
  de echte runtime-resultaten; eerdere mislukkingen blijven zichtbaar.
- De echte testcommandpoging duurde circa 12,6 seconden. Het terminale resultaat
  is achteraf uit dezelfde turn gelezen. Een afzonderlijke tussentijdse poll/yield
  is niet onafhankelijk vastgesteld; die specifieke runtimevariant is dus nog
  geen bewezen acceptatiepad. Niet-terminale metadata blijft Unknown in de tests.
- De echte Evaluation bleef bytegelijk in het API-readmodel na down/up. Alle
  disposable teststacks en hun eigen volumes zijn opgeruimd. De draaiende Aspire-
  omgeving en het bestaande Aspire-volume zijn niet herstart of gewijzigd.
- UI-gedrag, waaronder evidence voorbij item 100, is automatisch getest. Een
  aanvullende handmatige visuele browseracceptatie is in deze mijlpaal niet uitgevoerd.
