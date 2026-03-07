## Project
Colosseum — Agentic code review harness. Opinionated AI gladiators (Claude-powered) debate
GitHub PRs turn-by-turn, @mention each other, flag overlapping issues, and produce a ranked
verdict from an independent Arbiter.

## Tech Stack
- Runtime: C# / .NET 10
- Frontend: Blazor Server (SignalR real-time streaming)
- AI: Anthropic.SDK NuGet (claude-sonnet-4-6 default)
- GitHub: Octokit.NET
- Tests: xUnit + bUnit + Moq

## Structure
- Colosseum.Web/     — Blazor Server app (Pages, Components, Hubs)
- Colosseum.Core/    — Business logic (Models, Services, Configuration)
- Colosseum.Tests/   — xUnit tests (no real API calls — all mocked)

## Run
dotnet run --project Colosseum.Web/

## Test
dotnet test Colosseum.Tests/

## Build
dotnet build Colosseum.sln

## Lint
dotnet format --verify-no-changes

## Conventions
- All IDs are Guid
- Turn text hard-capped at 150 tokens (MaxTurnTokens config)
- Gladiators: Maximus (perf), Brutus (DRY), Cassius (DDD), Valeria (security), Arbiter (moderator)
- @mention regex: @([A-Za-z]+), validate against active gladiator names, strip self/unknown
- Issue format: [ISSUE: title | severity: high|medium|low]
- Overlap format: [OVERLAP: existing-issue-title]
- SimilarityThreshold default: 0.75, TrigramPrefilterThreshold default: 0.4
- Claude model for debate: claude-sonnet-4-6
- Claude model for rolling summaries: claude-haiku-4-5-20251001

## Memory
All session memory lives in .agent-memory/.
Session start: read CLAUDE.md -> progress.md -> decisions.log -> scan run.log
Session end: append run.log -> append decisions.log -> update progress.md -> append learnings.md -> git commit .agent-memory/
