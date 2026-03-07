## [2026-03-07] Design already prototyped in HTML
The index and arena pages are already fully designed as static HTML mockups with exact color
palette (CSS variables), fonts (Cinzel + Crimson Pro + JetBrains Mono), and component layout.
The Blazor components should faithfully reproduce this design — don't deviate from the visual
established in the prototypes.
---

## [2026-03-07] Gladiator accent colors (from prototype)
--maximus: #c94a0e (fire/orange)
--brutus:  #6a8fc7 (blue)
--cassius: #7a9e6a (green)
--valeria: #9a6ab0 (purple)
--arbiter: #c9a84c (gold)
These must be consistent across all components.
---

## [2026-03-07] Anthropic.SDK 5.x API differs significantly from docs/examples
`new Message { Role = RoleType.User, Content = ... }` object initializer does NOT work.
Use `new Message(RoleType.User, "text string")` constructor.
Use `response.Message.ToString()` not `.Content[0]` or TextBlock extraction.
Look up SDK via Context7 (`/anthropic/anthropic-sdk-dotnet`) before writing SDK code.
---

## [2026-03-07] .NET 10 Blazor template changed
`dotnet new blazorserver` no longer exists. Use `dotnet new blazor --interactivity Server --all-interactive`.
SignalR Client for Arena.razor requires `Microsoft.AspNetCore.SignalR.Client` NuGet (separate from server package).
---

## [2026-03-07] `dotnet new sln` creates `.slnx` on .NET 10
All `dotnet sln` commands must reference `Colosseum.slnx`, not `Colosseum.sln`.
---

## [2026-03-07] Build-test-fix loop required 3 iterations
Order of issues: (1) TextBlock API mismatch, (2) DebateTurn duplicate Id property,
(3) GladiatorVm type mismatch across components, (4) TrigramOverlap not public for tests.
Always run `dotnet build` + `dotnet test` before declaring done.
---
