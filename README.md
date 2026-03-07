# Colosseum

> Agentic code review harness — opinionated AI gladiators debate your code in real time.

Colosseum sends your GitHub PR through a squad of AI gladiators (Maximus, Brutus, Cassius, Valeria), each laser-focused on a specific quality axis. They debate turn-by-turn, @mention each other, flag overlapping issues, and produce a ranked verdict from an independent Arbiter.

## Stack

- **Runtime**: C# / .NET 10
- **Frontend**: Blazor Server + SignalR (real-time streaming)
- **AI**: Anthropic Claude API via `Anthropic.SDK`
- **GitHub**: Octokit.NET

## Setup

1. Clone the repo
2. Set your API key in `Colosseum.Web/appsettings.json`:
   ```json
   "Colosseum": {
     "AnthropicApiKey": "sk-ant-...",
     "GitHubToken": ""   // optional — required for private repos
   }
   ```
3. Run:
   ```bash
   dotnet run --project Colosseum.Web/
   ```
4. Open `https://localhost:5001`, paste a GitHub PR URL, choose your gladiators, click **Enter the Arena**.

## Build & Test

```bash
dotnet build Colosseum.slnx
dotnet test Colosseum.Tests/
```

## Gladiators

| Gladiator | Domain | Style |
|-----------|--------|-------|
| **Maximus** | Performance | Aggressive, cites Big-O |
| **Brutus** | DRY / YAGNI | Pedantic, hunts duplication |
| **Cassius** | DDD / SRP | Philosophical, guards boundaries |
| **Valeria** | Security | Paranoid, assumes breach |
| **Arbiter** | Moderator | Impartial, renders final verdict |

## Phase 2

See [COLOSSEUM-PHASE2.md](COLOSSEUM-PHASE2.md) for planned features: The Annals (persistence), The Forge (fix debate), The Barracks (custom gladiators), The Tribune (user interjection), The Proving Grounds (A/B testing).
