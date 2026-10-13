# Deterministic Evaluation v1 — evidence gate

**Update:** de gebruiker heeft de aanvullende capturebron goedgekeurd. De onderstaande
hook-only-bevindingen blijven geldig. Een runtime-read via app-server levert nu de
ontbrekende terminale status/exitcode met dezelfde tool-ID; zie
[de implementatie en het gebruik](deterministic-evaluation.md).

## Historisch resultaat van de hook-only-probe

Op 9 september 2026 is de eerste implementatiestap uitgevoerd met de geïnstalleerde
Codex CLI 0.153.4, .NET SDK 10.0.400 en de ongewijzigde Windows-repositoryhooks en
Collector. De probe stuurde alleen zijn eigen hooks naar een tijdelijke localhost
HTTP-ontvanger. Er is niets in de bestaande Aspire-database geschreven.

De echte opnames staan als gesaniteerde projecties in
`tests/Aigenterra.Codex.Tests/Fixtures/codex-0.153.4-command-hooks.json`.
De tool-ID's zijn gepseudonimiseerd en het lokale repositorypad in uitvoertekst is
vervangen door `C:\workspace\Aigenterra`. De lijst `keys` bewaart alle werkelijk
ontvangen top-level veldnamen. Overige metadatawaarden zijn bewust weggelaten;
deze projecties zijn dus geen volledige ingestfixtures en geen byte-identiek archief.

| Afzonderlijk uitgevoerd command | Terminal exitcode in de CLI | In de hook |
| --- | --- | --- |
| `dotnet build src/Aigenterra.Domain/Aigenterra.Domain.csproj --no-restore` | 0 | Alleen uitvoertekst |
| `dotnet build Aigenterra.Nonexistent.Probe.csproj --no-restore` | 1 | Alleen uitvoertekst |
| `dotnet test tests/Aigenterra.Domain.Tests/Aigenterra.Domain.Tests.csproj --no-restore` | 0; 24 tests geslaagd | Alleen uitvoertekst |
| `dotnet test Aigenterra.Nonexistent.Probe.csproj --no-restore` | 1 | Alleen uitvoertekst |

De CLI-exitcodes in deze tabel zijn onafhankelijke probe-observaties. Ze zijn
**geen door Aigenterra opgenomen evidence** en worden niet aan de fixtures toegevoegd.
De niet-bestaande projectpaden zijn bewust gekozen voor een echte niet-nul-exit,
niet voor het simuleren van falende testcases.

## Wat de hooks daadwerkelijk leveren

- `tool_name` is `Bash`, ook voor deze via exec uitgevoerde commands.
- PreToolUse en PostToolUse delen `tool_use_id`.
- `tool_input.command` bevat het oorspronkelijke command, zonder de door de
  runtime gebruikte PowerShell-wrapper.
- `tool_response` is een JSON-string met uitvoertekst, geen gestructureerd
  commandresultaat. Een exitcode of expliciete terminal-resultaatstatus ontbreekt.
- PostToolUse wordt in deze probe zowel bij exit 0 als exit 1 geleverd.
- De probe vroeg om een korte yield, maar deze vier commands leverden geen
  afzonderlijk gepolld resultaat op. De delayed-result-route is nog niet bewezen.

Dit past bij het [officiële hookcontract](https://learn.chatgpt.com/docs/hooks#posttooluse),
dat tool-specifieke output beschrijft maar geen universele exitcode garandeert.
De feitelijke fixture, niet een verondersteld toekomstig schema, bepaalt de grens
voor de geïnstalleerde runtime.

## Consequentie vóór goedkeuring van aanvullende capture

Commandherkenning en correlatie zijn mogelijk. `Succeeded` en `Failed` zijn met
deze payloads niet verantwoord volgens het gekozen v1-bewijsniveau. Teksten zoals
`Build succeeded.` of `Passed!` mogen de ontbrekende exitcode niet vervangen.
Evenmin mogen prompttekst, de eindclaim van de agent of de exitcode van het
Collector-proces als commandresultaat worden gebruikt.

De hook-only evidence gate slaagde daarom niet. Op dat beslismoment was nog geen
evaluator-, API-, UI- of persistencecode toegevoegd en geen migration gemaakt.
Aanvullende capture vereiste een afzonderlijke scopebeslissing; de gebruiker heeft
die inmiddels goedgekeurd. De daaropvolgende implementatie en acceptatie staan in
[Deterministic Evaluation](deterministic-evaluation.md).

Een evaluator die uitsluitend Unknown teruggeeft voor deze resultaatpayloads kan
technisch worden gebouwd, maar zou de afgesproken productacceptatie niet halen.
De hook-only-probe alleen geldt daarom niet als productacceptatie.
