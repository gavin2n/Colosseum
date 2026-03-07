# 🏟️ Colosseum — Project Specification (v2)

> Agentic code review harness: opinionated AI gladiators debate your code in real time.

---

## 🎯 Problem / Solution / Outcome

```
Problem:   Code review is shallow, slow, and misses domain-specific concerns. Reviewers
           have broad knowledge but rarely enforce deep opinions across all quality axes
           simultaneously — DRY, performance, security, DDD, SRP all at once. Worse,
           reviewers rarely challenge each other, so contradictory advice goes unresolved.

Solution:  Colosseum — a configurable squad of opinionated AI gladiators, each laser-
           focused on a specific domain, who review GitHub PRs, debate their findings
           turn-by-turn, @mention each other to challenge or support specific positions,
           flag overlapping issues for potential merge, and produce a ranked issue list
           moderated by an independent final arbiter who resolves all open conflicts.

Outcome:   Developers receive a structured, multi-perspective, prioritised review with
           genuine conflict resolution — not a flat checklist. Issues are ranked by
           consensus, overlapping issues are merged or distinguished, dissenting views
           are preserved with clear attribution.
```

---

## 🏗️ Approach & Implementation Strategy

- **Language / Runtime**: C# / .NET 10
- **Frontend**: Blazor Server (real-time SSR with SignalR for turn-by-turn streaming)
- **AI Integration**: Anthropic Claude API via `Anthropic.SDK` NuGet package
- **GitHub Integration**: Octokit.NET for PR diff fetching
- **State Management**: In-process session state (no DB for v1; file-based persistence optional)
- **Configuration**: `appsettings.json` + runtime UI config for gladiator squad composition
- **Testing**: xUnit + bUnit (Blazor component tests) + Moq
- **Build**: `dotnet build`, `dotnet test`, `dotnet run`
- **Constraints**:
  - Claude API calls are turn-based — one gladiator speaks, waits, next speaks
  - Each gladiator turn must be short: 2–4 sentences max per turn, enforced via prompt
  - GitHub token required for private repos; public repos unauthenticated
  - No LLM parallelism in debate phase — sequential by design for dramatic effect

---

## 🧱 Architecture

```
Colosseum/
├── Colosseum.Web/
│   ├── Pages/
│   │   ├── Index.razor             # Landing — enter GitHub PR URL + configure squad
│   │   └── Arena.razor             # Live debate view (real-time turn feed + verdict)
│   ├── Components/
│   │   ├── GladiatorConfig.razor   # Squad builder UI
│   │   ├── DebateFeed.razor        # Chat-room feed with @mention chips
│   │   ├── IssueList.razor         # Ranked issues with merge badges + detail drawers
│   │   └── VerdictPanel.razor      # Arbiter verdict with merge resolutions
│   ├── Hubs/
│   │   └── ArenaHub.cs             # SignalR hub
│   └── Program.cs
│
├── Colosseum.Core/
│   ├── Models/
│   │   ├── Gladiator.cs
│   │   ├── DebateTurn.cs           # + Mentions (List<string>), ReferencedIssueIds
│   │   ├── Issue.cs                # + MergeCandidate (Guid?), DebateTrail
│   │   ├── IssueDebateEntry.cs     # GladiatorId, Stance enum, Text, Timestamp
│   │   ├── ReviewSession.cs
│   │   └── Verdict.cs              # + MergedIssues (List<MergeResolution>)
│   ├── Services/
│   │   ├── GitHubService.cs
│   │   ├── GladiatorService.cs     # Builds prompts incl. @mention + overlap instructions
│   │   ├── MentionParser.cs        # Extracts @Name tokens; validates against squad
│   │   ├── SimilarityService.cs    # Trigram → Claude two-step merge candidate detection
│   │   ├── IssueTracker.cs         # Record, vote, merge-flag, [OVERLAP:] parsing
│   │   ├── DebateOrchestrator.cs   # Turn loop; wires parsing pipeline; emits events
│   │   └── ModeratorService.cs     # Verdict: resolve merges, rank, dismiss, summarise
│   └── Configuration/
│       └── ColosseumOptions.cs     # + SimilarityThreshold (default 0.75)
│
└── Colosseum.Tests/
    ├── MentionParserTests.cs
    ├── SimilarityServiceTests.cs
    ├── IssueTrackerTests.cs
    ├── DebateOrchestratorTests.cs
    ├── GladiatorServiceTests.cs
    └── ModeratorServiceTests.cs
```

---

## 💬 @Mention System

### How it works

Each gladiator's system prompt instructs them to address others directly using `@Name`
when responding to a specific point. The orchestrator parses all turns for mentions and
stores them as structured metadata on `DebateTurn.Mentions`.

**Instruction added to every gladiator prompt:**
> "When responding to a point made by another gladiator, address them directly using
> @Name (e.g. @Brutus, @Maximus). You may support or challenge their position. Keep
> your turn to 2–3 sentences. If your concern overlaps an existing raised issue, use
> [OVERLAP: issue-title] and explain whether you agree, disagree, or see a distinction."

### Orchestrator behaviour

- `MentionParser.Extract(turnText, activeGladiators)` → `List<string>` mentions
- Self-mentions and unknown names stripped silently
- `DebateTurn.Mentions` populated before broadcast
- The **next** gladiator in turn order receives full history with mentions annotated —
  creating natural cross-gladiator escalation threads without extra API calls
- The current open issue list is injected into every prompt so gladiators can
  reference existing issues by title rather than restating them

### Mention rendering rules (UI)

- @mentions in message text replaced with coloured inline chips using the target
  gladiator's colour
- Unknown/stripped mentions rendered as plain text
- A message with no @mentions renders normally (first-round opening arguments rarely
  have targets)

---

## 🔀 Issue Merge Candidate System

### Detection pipeline (SimilarityService)

After each new issue is raised, `IssueTracker` runs similarity detection:

```
Step 1 — Trigram overlap (pure C#, no API call):
    score = trigramOverlap(newIssue.Title, existingIssue.Title)
    if score < 0.4 → not similar, skip Step 2
    if score = 1.0 → identical, mark candidate immediately

Step 2 — Semantic check (one Claude API call):
    prompt: "Are these two code review issues describing the same problem?
             Issue A: {title + description}
             Issue B: {title + description}
             Reply ONLY with JSON: { similar: bool, confidence: float, reason: string }"
    if similar = true AND confidence >= SimilarityThreshold (0.75):
        → mark as merge candidate pair
```

### What "flagged" means

- Both issues get `MergeCandidate` set to each other's `Guid`
- Both get a `IssueDebateEntry` added to their trail:
  `"[System] Flagged as potential duplicate of: [other issue title]"`
- UI shows ⚭ badge on both cards; clicking navigates to the counterpart

### What does NOT happen mid-debate

- No automatic merging — issues remain separate throughout the debate
- Gladiators are informed via the issue list in their prompt, so they may naturally
  address the overlap themselves (e.g. `@Brutus I think your duplication concern
  is the same as my N+1 issue — both stem from the missing abstraction`)
- The ⚭ flag is informational only until the Arbiter rules

### Arbiter merge resolution

The Arbiter's prompt instructs it to explicitly rule on every flagged pair:

| Decision | Meaning | Verdict output |
|----------|---------|----------------|
| **Merge** | Same problem, one fix | Single combined finding with both gladiators credited |
| **Distinguish** | Related but different | Both kept; a note explains the distinction |
| **Dismiss one** | One is a subset of the other | Weaker one dismissed; noted in verdict |

---

## 🔄 Updated Data Flow

```
User enters PR URL + squad config
    → GitHubService fetches unified diff

    → DebateOrchestrator.RunAsync(session, cancellationToken):

        For each round (1–3, default 2):
            For each active Gladiator in squad order:

                [Prompt assembly]
                prompt = GladiatorService.BuildPrompt(gladiator,
                    diff, priorTurns, openIssues, activeGladiatorNames)

                [Claude API call]
                turnText = await claude.CompleteAsync(prompt, maxTokens: 150)

                [Parsing pipeline]
                mentions = MentionParser.Extract(turnText, activeGladiators)
                newIssues, overlaps = IssueParser.Extract(turnText)

                For each newIssue:
                    IssueTracker.Record(newIssue, gladiatorId)
                    candidates = SimilarityService.FindCandidates(newIssue, session.Issues)
                    if candidates.Any(): IssueTracker.FlagMergeCandidate(newIssue, candidate)

                For each overlap tag [OVERLAP: title]:
                    IssueTracker.RecordOverlapSignal(gladiatorId, title, turnText)

                IssueTracker.ParseStanceSignals(turn)
                    → infer Supports/Disputes from "@Name is correct/wrong" language

                turn = new DebateTurn { ..., Mentions = mentions }
                emit turn via IProgress<DebateTurn>
                emit updated issues via IProgress<IssueUpdate>

        emit DebateComplete

    → ModeratorService.RenderVerdict(session):
        singleClaudeCall(arbiterPersona + fullTranscript + allIssues + mergePairs)
        → Verdict { RankedIssues, MergedIssues, DismissedIssues, Summary }

    → Broadcast VerdictReady → VerdictPanel renders
```

---

### Gladiator Personas (v1 Squad)

| Gladiator | Domain | Personality | @mention style |
|-----------|--------|-------------|----------------|
| **Maximus** | Performance | Aggressive, cites Big-O, hates allocations | Confrontational: _"@Cassius your service boundary is elegant but useless if it's O(n²)"_ |
| **Brutus** | DRY / YAGNI / Clean Code | Pedantic, quotes Fowler, hunts duplication | Dismissive of over-engineering: _"@Cassius that's three layers for a two-line fix"_ |
| **Cassius** | DDD / Clean Abstractions / SRP | Philosophical, guards domain boundaries | Patient but firm: _"@Brutus the duplication is a symptom — the missing boundary is the disease"_ |
| **Valeria** | Security | Paranoid, assumes breach | Urgent: _"@Maximus performance is irrelevant if the service is already compromised"_ |
| **Arbiter** | Moderator | Calm, impartial | Verdict only — never participates in debate |

---

## ✅ Implementation Tasks

```
[ ] 1. Solution scaffold
    - Files: Colosseum.sln, Colosseum.Web/, Colosseum.Core/, Colosseum.Tests/
    - What: dotnet new solution, three projects, project references wired
    - Edge cases: Blazor Server (not WASM) template

[ ] 2. Core models
    - Files: Colosseum.Core/Models/*.cs
    - What: Gladiator, DebateTurn (+Mentions, ReferencedIssueIds), Issue (+MergeCandidate Guid?,
            DebateTrail List<IssueDebateEntry>, Status enum), IssueDebateEntry (Stance enum:
            Raised|Supports|Disputes|Seconds), ReviewSession, Verdict (+MergedIssues,
            MergeResolution record with Decision enum: Merge|Distinguish|DismissOne)
    - Edge cases: all Guid IDs; Status must include MergeCandidate as distinct state

[ ] 3. ColosseumOptions config
    - Files: Colosseum.Core/Configuration/ColosseumOptions.cs, appsettings.json
    - What: AnthropicApiKey, GitHubToken, MaxRounds (2), MaxTurnTokens (150),
            SimilarityThreshold (0.75), TrigramPrefilterThreshold (0.4), default squad
    - Edge cases: validate SimilarityThreshold is 0.0–1.0 at startup

[PARALLEL] [ ] 4. GitHubService
    - Files: Colosseum.Core/Services/GitHubService.cs
    - What: parse PR URL → owner/repo/number; fetch unified diff via Octokit.NET
    - Edge cases: private repo; not found; diff > 8k tokens → truncate + append notice

[PARALLEL] [ ] 5. MentionParser
    - Files: Colosseum.Core/Services/MentionParser.cs
    - What: regex @([A-Za-z]+) on turn text; validate against active gladiator name list
            (case-insensitive); strip self-mentions; strip unknowns; deduplicate
    - Why: structured mention metadata for UI + prompt context
    - Edge cases: @Arbiter is valid (gladiator can appeal to the moderator);
                  embedded in punctuation: "@Brutus," → "Brutus" extracted correctly

[PARALLEL] [ ] 6. GladiatorService
    - Files: Colosseum.Core/Services/GladiatorService.cs
    - What: per-gladiator system prompt builder. Prompt sections:
            1. Persona + domain rules
            2. @mention instruction + active gladiator name list
            3. Issue flag format: [ISSUE: title | severity: high|medium|low]
            4. Overlap signal format: [OVERLAP: existing-issue-title]
            5. Short-turn constraint (≤3 sentences)
            Context injected each turn:
            - Code diff (truncated if needed)
            - Prior turns (full history, mentions annotated as "(mentioned @X)")
            - Current open issues list (id + title + raiser)
    - Edge cases: regenerate prompt with updated gladiator list if squad changes;
                  open issues list omitted on round 1 turn 1 (nothing raised yet)

[ ] 7. SimilarityService
    - Files: Colosseum.Core/Services/SimilarityService.cs
    - What: FindCandidates(newIssue, existingIssues) → List<(Issue, float score)>
            Step 1: trigram overlap on titles (pure C#)
            Step 2: Claude call if trigram > 0.4, parse JSON response
            Returns candidates where final score ≥ SimilarityThreshold
    - Edge cases: identical title → score 1.0, skip Step 2; Claude JSON malformed →
                  log warning, return empty (fail safe — never false-positive merge);
                  max 1 Claude call per pair per session (cache results)

[ ] 8. IssueTracker
    - Files: Colosseum.Core/Services/IssueTracker.cs
    - What:
        Record(issue, gladiatorId): add issue; create Raised entry; run similarity
        FlagMergeCandidate(issueA, issueB): set MergeCandidate on both; add system
            trail entries; set Status = MergeCandidate on both
        RecordStance(issueId, gladiatorId, stance, text): add trail entry; adjust votes
        ParseAndApply(turn, gladiatorId): scan for [ISSUE:], [OVERLAP:], and stance
            signals in natural language ("@X is correct about", "I dispute @X's point")
    - Edge cases: [OVERLAP: title] with unknown title → fuzzy match against existing;
                  if no match, ignore tag silently

[ ] 9. DebateOrchestrator
    - Files: Colosseum.Core/Services/DebateOrchestrator.cs
    - What: RunAsync(session, progress, cancellationToken)
            Full pipeline per turn: prompt build → Claude call → MentionParser →
            IssueParser → SimilarityService → IssueTracker → emit events
    - Edge cases: Claude error → skip turn, log, continue; CancellationToken at every await;
                  emit partial state on cancellation so UI shows what completed

[ ] 10. ModeratorService
    - Files: Colosseum.Core/Services/ModeratorService.cs
    - What: RenderVerdict(session) → Verdict
            Single Claude call: Arbiter persona + transcript + issues + merge pairs
            Prompt instructs structured JSON output:
            {
              mergeResolutions: [{ issueAId, issueBId, decision, reason }],
              rankedIssues: [{ issueId, rank, rationale }],
              dismissedIssues: [{ issueId, reason }],
              summary: "paragraph1\n\nparagraph2"
            }
    - Edge cases: JSON parse failure → store raw text in Verdict.RawSummary and
                  set Verdict.ParseFailed = true; UI shows raw text gracefully

[ ] 11. SignalR ArenaHub
    - Files: Colosseum.Web/Hubs/ArenaHub.cs
    - What: StartReview → kicks off orchestrator; pushes DebateTurn (with Mentions),
            IssueAdded, IssueUpdated (votes + MergeCandidate flag), VerdictReady
    - Edge cases: disconnect → cancel; reconnect → client can call GetSessionState()
                  to replay all turns and issues for the current session

[ ] 12. Index.razor
    - Files: Colosseum.Web/Pages/Index.razor
    - What: PR URL + GladiatorConfig + rounds slider + Enter button
    - Edge cases: URL format validation; min 2 gladiators enforced

[ ] 13. GladiatorConfig.razor
    - Files: Colosseum.Web/Components/GladiatorConfig.razor
    - What: toggle cards per gladiator; emits squad config changes
    - Edge cases: shake animation when attempting to go below 2 active

[ ] 14. Arena.razor
    - Files: Colosseum.Web/Pages/Arena.razor
    - What: SignalR connection; DebateFeed + IssueList side-by-side (desktop) /
            tab-switched (mobile); VerdictPanel on VerdictReady; Stop button
    - Edge cases: reconnect handling; loading skeleton; mobile bottom tab bar

[ ] 15. DebateFeed.razor
    - Files: Colosseum.Web/Components/DebateFeed.razor
    - What: chat-room style; consecutive-message avatar collapse; @mention chips
            coloured by target gladiator; round separator dividers; rebuttal
            left-border accent; auto-scroll; typing indicator per gladiator
    - Edge cases: @mention chip for stripped/unknown mention → plain text fallback

[ ] 16. IssueList.razor
    - Files: Colosseum.Web/Components/IssueList.razor
    - What: ranked cards with expandable debate trail drawers (stance badges:
            raised/supports/disputes/seconds); ⚭ merge candidate badge with link
            to counterpart card; dismissed cards greyed + strikethrough; vote bars
    - Edge cases: ⚭ badge added dynamically without full list re-render;
                  counterpart card link scrolls to target and briefly highlights it

[ ] 17. VerdictPanel.razor
    - Files: Colosseum.Web/Components/VerdictPanel.razor
    - What: sections: Merge Resolutions | Ranked Issues | Dismissed | Summary |
            Export as Markdown
            Merge section shows each pair + Arbiter's decision (Merge/Distinguish/Dismiss)
    - Edge cases: empty sections hidden; ParseFailed → show raw text with warning banner

[ ] 18. Wire DI + Program.cs
    - Files: Colosseum.Web/Program.cs
    - What: register all services; bind config; fail fast on missing API key
    - Edge cases: clear startup error for missing AnthropicApiKey

[ ] 19. xUnit tests
    - Files: Colosseum.Tests/*.cs
    - What:
        MentionParserTests: valid mention; self stripped; unknown stripped;
            case-insensitive; multiple in one turn; @Arbiter valid; punctuation-embedded
        SimilarityServiceTests: identical → 1.0 no API call; unrelated → 0.0 no API call;
            trigram > 0.4 but semantic low → no candidate; mocked Claude similar=true → candidate
        IssueTrackerTests: raise; vote; merge flag; [OVERLAP:] parse; fuzzy title match;
            stance signals from natural language
        DebateOrchestratorTests: full pipeline with mocked Claude; mention extraction;
            prompt includes open issues list from turn 2 onward
        ModeratorServiceTests: merge Merge/Distinguish/DismissOne paths; JSON parse failure
            fallback; zero-issue clean verdict
    - Edge cases: mock ALL Claude calls — no real API calls in test suite
```

---

## 🧪 Verification & Testing

```
Build:   dotnet build Colosseum.sln
Test:    dotnet test Colosseum.Tests/
Lint:    dotnet format --verify-no-changes
E2E:     Manual browser smoke test against a public GitHub PR
```

---

## 🎯 Acceptance Criteria

```
[ ] Gladiator turns contain @Name mentions when responding to other gladiators
[ ] @mentions render as coloured inline chips in the debate feed
[ ] Consecutive messages from same gladiator collapse the avatar (chat-room style)
[ ] When two issues score ≥ 0.75 similarity, both cards show ⚭ badge
[ ] ⚭ badge is a link — clicking navigates to the counterpart issue card
[ ] Arbiter verdict contains explicit Merge/Distinguish/Dismiss ruling for each ⚭ pair
[ ] Verdict merge section attributes both gladiators on a merged finding
[ ] Gladiators reference existing open issues in their prompts from turn 2 onward
[ ] User can enter a public GitHub PR URL and click Enter the Arena
[ ] Squad defaults to all 4 gladiators; min 2 enforced
[ ] Debate turns appear one at a time, colour-coded per gladiator
[ ] Each turn ≤ ~3 sentences
[ ] Issues live-update in sidebar; votes animate on change
[ ] Arbiter verdict renders after all rounds with ranked + merged + dismissed sections
[ ] Export as Markdown downloads a complete .md file
[ ] Stop cancels cleanly — no orphaned API calls
[ ] Invalid PR URL shows friendly error
[ ] Missing API key shows actionable startup error
```

---

## 📝 Documentation

- `README.md` — setup, config, screenshot placeholder
- `docs/gladiators.md` — persona definitions, @mention style, how to add a gladiator
- `docs/architecture.md` — data flow, similarity pipeline, merge resolution lifecycle

---

## 🚧 Out of Scope for v1

- Database / session persistence
- User accounts / auth
- GitHub OAuth
- Custom gladiator builder UI
- Writing review comments back to GitHub PR
- Multiple simultaneous sessions
- Real-time @mention push (mentioned gladiator gets extra turn mid-round)
