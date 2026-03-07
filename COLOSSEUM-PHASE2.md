# 🏟️ Colosseum — Phase 2 Feature Spec

> Brainstorm & specification addendum for post-v1 development.
> Each feature area is scoped, named, and given an initial architecture sketch.

---

## Naming Conventions Established in This Doc

| Phase 1 concept | Phase 2 name |
|-----------------|--------------|
| Review session output | **The Verdict Scroll** (post-mortem / after action report) |
| Phase 2 fix debate | **The Forge** — gladiators debate and produce fixes |
| Custom gladiator builder | **The Barracks** — recruit and train new gladiators |
| Persistence / history | **The Annals** — scrolls of past battles |
| User participation | **The Tribune** — spectator who may speak |

---

## 1. 🪙 Token Efficiency

### Problem
Each gladiator turn currently receives the full debate history + full diff + full issue list.
As debates grow (2 rounds × 4 gladiators = 8 turns minimum), context balloons fast — both
in cost and latency. A 300-line diff with a 6-turn history can easily hit 6–8k tokens per
call.

### Proposed Approach: Tiered Context Injection

```
Full diff:
  - Round 1 only — all gladiators see the full diff on their first turn
  - Round 2+ — inject only the diff sections (hunks) relevant to issues already raised
    (map issues to line ranges; extract ±10 lines of context around each)

Debate history:
  - Full history for round 1 (nothing to summarise yet)
  - Round 2+: inject a rolling summary of prior turns (generated once per round boundary)
    rather than the raw transcript. Keep raw transcript for the Arbiter only.

Issue list:
  - Always injected in full (it's small — typically <500 tokens regardless)

Rolling summary generation:
  - After each round completes, a lightweight Claude call (haiku/small model) produces
    a 150-word summary: "Issues raised so far, key agreements, key disputes."
  - This summary replaces the raw history for subsequent gladiator prompts
  - The full raw transcript is preserved separately for the Arbiter and the Verdict Scroll
```

### Token budget targets (per turn)

| Component | Round 1 | Round 2+ |
|-----------|---------|---------|
| System persona | ~200 | ~200 |
| Diff | full (~2–4k) | relevant hunks (~500) |
| History | none | rolling summary (~200) |
| Issue list | 0 (empty) | ~300 |
| **Total** | **~2.5–4.5k** | **~1.2k** |

### New service needed
`ContextCompressor` — responsible for:
- Hunk extraction (map issue line references to diff ranges)
- Rolling summary generation (one call per round boundary, cacheable)
- Budget enforcement: if compressed context still > threshold, truncate diff further
  with a notice appended

### Config additions
```json
"TokenBudgetPerTurn": 2000,
"UseRollingSummary": true,
"RollingSummaryModel": "claude-haiku-4-5"
```

---

## 2. ⚔️ The Barracks — Custom Gladiator Skill System

### Problem
v1 has 4 hardcoded gladiators. Teams will want to add their own domain experts:
an Accessibility gladiator, a Cost gladiator, a Domain-specific rules gladiator
("our microservice never calls X directly"), a Compliance gladiator, etc.

### Proposed Approach: Gladiator-as-Skill

Each custom gladiator is a `.gladiator` file (a YAML/markdown hybrid, similar to
the `.skill` format) that the system loads at startup:

```yaml
# barracks/accessibility.gladiator
name: Aelia
domain: Accessibility / WCAG
personality: >
  Meticulous, advocates for users who are overlooked. Quietly furious about
  missing aria labels. Cites WCAG 2.2 criteria by number. Never lets "it works
  on my machine" slide when a screen reader user can't navigate it.
mention_style: >
  "@Brutus clean code means nothing if a blind user can't reach the button."
focus_areas:
  - ARIA attributes and roles
  - Keyboard navigation and focus management
  - Colour contrast ratios
  - Screen reader compatibility
  - Touch target sizes (mobile)
severity_bias: high   # This gladiator tends to rate issues as high severity
max_rounds: 2         # Can be capped per gladiator to save tokens
```

### The Barracks UI (config screen extension)
- "Recruit a Gladiator" button on the Index page squad builder
- Form fields matching the YAML schema
- Preview panel showing how the gladiator would introduce themselves
- Export as `.gladiator` file; import from file
- Built-in gladiators shown as "Legion" (locked, read-only); custom ones as "Mercenaries"

### System changes
- `GladiatorService` reads from both hardcoded personas AND a `barracks/` folder
- Gladiator loader validates required fields at startup
- Custom gladiator names added to MentionParser's active list automatically
- Skills documentation page: `docs/barracks.md` with gladiator writing guide

---

## 3. 📜 The Annals — Persistence Layer

### Problem
Every session is currently in-memory and lost on restart. Users want to:
- Browse past reviews by repo/PR
- Compare reviews of the same PR across different squad configurations
- Track whether issues from previous reviews recur in future PRs

### Storage approach (progressive)

**Phase 2a — File-based (SQLite via EF Core)**
```
Colosseum.Data/
├── ColosseumDbContext.cs
├── Migrations/
└── Entities/
    ├── ReviewRecord.cs     # PR metadata, date, squad used, status
    ├── TurnRecord.cs       # All debate turns, linked to ReviewRecord
    ├── IssueRecord.cs      # All issues, with debate trail as JSON blob
    └── VerdictRecord.cs    # Final verdict, merge resolutions, export markdown
```

**Phase 2b — Optional cloud backend**
- PostgreSQL via connection string swap
- No code changes required (EF Core abstraction)

### The Annals UI
- New top-level route: `/annals`
- Table of past reviews: PR title, repo, date, squad, issue count, severity breakdown
- Click → full replay of the debate (read-only Arena view, no live SignalR)
- Filter by repo, gladiator squad, severity, date range
- "Compare" button: side-by-side diff of two reviews of the same PR

### Recurrence detection
- On each new session start, query Annals for prior reviews of the same repo
- If recurring issues found (matched by SimilarityService), show a warning banner:
  **"3 issues from this repo's last review remain unresolved"**
- Link to prior verdict for context

---

## 4. 🎭 The Tribune — User Interjection System

### Problem
The debate currently runs autonomously. Sometimes the user has context the gladiators
lack — a business constraint, a deliberate tradeoff, a "we know about this but it's
intentional." Currently there's no way to feed that in without stopping the debate.

### Design Principles
- **All human interaction is optional** — the debate runs perfectly without it
- The Tribune never blocks a gladiator turn — interjections are queued and injected
  at natural turn boundaries
- Users can speak *to* the arena, *to* a specific gladiator, or *close* debate on
  an issue

### Three interjection modes

#### Mode 1: Arena Statement (broadcast)
User types into a text box at the bottom of the debate feed. Submitted message
is injected as a special turn in the feed, styled distinctly (gold, crown icon):

```
👑 Tribune: "The N+1 issue is known — we're migrating to CQRS next sprint.
             Focus on the security concerns."
```

All gladiators receive this statement at the start of the next turn as:
`[Tribune statement: "The N+1 issue is known — migration in progress. Focus on security."]`

#### Mode 2: Direct Address (@gladiator)
User can @mention a specific gladiator in their statement. That gladiator receives
the message as a direct prompt addition on their very next turn:
`[The Tribune addresses you directly: "Valeria — can you elaborate on the PCI risk?"]`

The addressed gladiator's next turn must open by responding to the Tribune before
continuing their own analysis.

#### Mode 3: Close Issue
Each issue card has an "⛔ Close" button. Clicking it:
- Marks the issue as `ClosedByTribune` (distinct from Dismissed)
- Injects a system message into the debate: `[Tribune closed debate on: "N+1 query in retry loop"]`
- All gladiators are instructed to not raise or reference this issue further
- The Arbiter notes it in the verdict as "Closed by reviewer — not in scope for this review"

### Gladiator questions to the user

Gladiators can request clarification by emitting a structured `[QUESTION: text]` tag:

```
[QUESTION: Is the RetryPaymentCommand handler ever called from a context
where the idempotency key is already validated upstream?]
```

This surfaces in the UI as a **"Question for you"** notification card in the issue
sidebar, attributed to the gladiator who asked. The user can:
- Answer (injected as Tribune statement directed at that gladiator)
- Dismiss (gladiator proceeds without the answer)
- Ignore entirely (debate continues; question expires after the next round)

Questions are capped at **1 per gladiator per round** to prevent interrogation loops.

### New components
- `TribuneInput.razor` — message box + @mention autocomplete at bottom of feed
- `QuestionCard.razor` — surfaced in issue sidebar with answer/dismiss actions
- `TribuneMessage` model — distinct turn type, styled separately in feed
- `DebateOrchestrator` changes: checks interjection queue before each turn;
  injects if present; handles ClosedByTribune issue filter

---

## 5. 📋 The Verdict Scroll — GitHub PR Integration

### Problem
The review output currently lives only inside Colosseum. The actual value is
on the PR — developers need the findings where they work, not in a separate tool.

### Two output modes

#### Mode 1: PR Check (pass/fail status)
Post a GitHub Check Run on the PR commit:

```
Check name:   Colosseum Review
Status:       completed
Conclusion:   failure (if any HIGH severity confirmed issues)
              neutral (if only MED/LOW or all dismissed)
              success (if no issues or all closed/dismissed)
Summary:      "4 issues found (2 HIGH, 1 MED, 1 dismissed).
               See full Verdict Scroll below."
Details URL:  https://your-colosseum-instance/annals/{sessionId}
```

Requires: GitHub App installation (or PAT with `checks:write` scope).

#### Mode 2: Line-level PR Comments
For each confirmed issue that has a line reference (extracted from the debate turn
where it was raised), post a review comment on the specific line:

```
GitHub review comment on line 47 of PaymentProcessor.cs:
┌─────────────────────────────────────────────────────────┐
│ ⚔ Colosseum · Maximus (Performance) · HIGH              │
│                                                          │
│ N+1 query in retry loop                                  │
│                                                          │
│ GetPendingAsync() is called inside a retry loop with     │
│ no batch strategy. Every retry hits the DB individually. │
│                                                          │
│ Consensus: 2 for, 0 against                              │
│ Full debate: https://colosseum/annals/{id}#issue-1       │
└─────────────────────────────────────────────────────────┘
```

For merge-candidate issues that were combined by the Arbiter, the comment
credits both gladiators and links to the merge resolution.

Dismissed issues are **not** posted as PR comments — only confirmed findings.

#### Mode 3: Summary PR Comment
A single top-level PR review comment containing the full Verdict Scroll as markdown:
- Overview table (issue, severity, domain, consensus)
- Merge resolutions section
- Arbiter summary paragraphs
- "Reviewed by Colosseum · Squad: Maximus, Brutus, Cassius, Valeria"

### Line reference extraction
`IssueParser` needs to extract line references when gladiators cite them:
- Pattern: `line 47`, `line 47–52`, `lines 83-91`, `L47`, `at line 47`
- Stored as `Issue.LineReference` (nullable string: `"47"` or `"83-91"`)
- File reference extracted from context (the diff hunk the gladiator was responding to)
- If no line reference: comment posted at the file level, not line level

### New services
- `GitHubReviewService` — posts check runs, review comments, summary comment
- Requires new config: `GitHubAppId`, `GitHubAppPrivateKey` (or `GitHubPat`)
- New `appsettings.json` section: `"GitHubReview": { "PostCheck": true, "PostLineComments": true, "PostSummary": true }`

---

## 6. 🔨 The Forge — Fix Debate Phase

### Concept
After the Verdict Scroll is complete, users can optionally enter **The Forge** —
a second agentic phase where gladiators stop finding problems and start solving them.

### Name: The Forge
*"Where iron is shaped into something useful."*

The Forge is a separate workflow triggered from the VerdictPanel:
**"Enter the Forge →"** button, available after verdict renders.

### How it works

Each confirmed issue from the Verdict Scroll enters the Forge as a **Smithing Task**.
Gladiators now take on a collaborative (rather than adversarial) mode:

```
Forge prompt instruction (replaces debate instruction):
"You are now in The Forge. Your role is not to find problems but to fix them.
For each assigned issue, propose a concrete code change. Be specific — name the
exact method, class, or pattern to use. If another gladiator has already proposed
a fix, build on it, refine it, or flag a conflict. Keep your turn to 3 sentences."
```

### Turn structure in The Forge

Each issue gets its own **Smithing Round**:

```
For each confirmed issue (in ranked order):
    Round 1: Each gladiator proposes or contributes to a fix approach
    Round 2: Gladiators @mention each other to refine, challenge, or second proposals
    → Arbiter synthesises the Forge debate into a Fix Recommendation:
        - Recommended approach (with code sketch if possible)
        - Dissenting approaches (if meaningful)
        - Files to change
        - Estimated effort (S/M/L)
```

### The Forge output: Fix Tickets
Each issue produces a **Fix Ticket** — added to the Verdict Scroll as a new section:

```markdown
## 🔨 Fix Ticket #1 — N+1 query in retry loop

**Recommended fix** (Forge consensus):
Replace the per-item GetPendingAsync() call with a batched
GetPendingBatchAsync(ids) call outside the retry loop.
Cache idempotency key reads using IMemoryCache with a 60s TTL.

**Files to change:**
- PaymentRepository.cs — add GetPendingBatchAsync()
- PaymentProcessor.cs:47 — refactor retry loop

**Effort:** Medium

**Dissent (Cassius):** Consider whether the retry loop itself
should move into a domain service at the same time.
```

### Forge vs Debate: key differences

| | The Arena (Debate) | The Forge (Fix) |
|---|---|---|
| Goal | Find problems | Fix problems |
| Gladiator stance | Adversarial | Collaborative |
| Output | Issue list + verdict | Fix tickets |
| Arbiter role | Rank + merge + dismiss | Synthesise fix approach |
| User interjection | Optional | Strongly encouraged ("use our BaseRepository pattern") |
| GitHub output | PR check + review comments | Can optionally open a draft PR with suggested changes |

### Optional: Forge → Draft PR
If GitHub integration is enabled, the Forge can open a GitHub Draft PR against the
reviewed branch containing:
- One commit per Fix Ticket
- Commit message: `[Colosseum Forge] Fix: {issue title}`
- Stub implementation matching the Fix Ticket recommendation (code sketch)
- Marked as Draft — human must review, complete, and promote

This is a **phase 3 feature** — listed here for visibility but intentionally not
scoped for phase 2 implementation.

---

## 📦 Phase 2 — Recommended Build Order

Given dependencies, the suggested implementation sequence:

```
Sprint 1 (foundations):
  [ ] Token efficiency / ContextCompressor
  [ ] Persistence layer — SQLite/EF Core (The Annals data model)
  [ ] The Annals UI — list + replay views

Sprint 2 (enrichment):
  [ ] The Barracks — .gladiator file format + loader + UI
  [ ] Line reference extraction in IssueParser
  [ ] GitHub PR Check posting

Sprint 3 (interactivity):
  [ ] The Tribune — Arena Statement + Close Issue
  [ ] Gladiator questions to Tribune
  [ ] GitHub line-level PR comments + summary comment

Sprint 4 (The Forge):
  [ ] The Forge workflow (separate route + orchestrator mode)
  [ ] Fix Ticket model + Arbiter Forge synthesis
  [ ] Forge output appended to Verdict Scroll
  [ ] Forge → GitHub Draft PR (stretch goal)
```

---

## 🚧 Explicitly Out of Scope (Phase 2)

- Real-time multi-user collaboration (multiple Tribunes)
- AI-generated test cases alongside fixes
- IDE plugin / VS Code extension (separate product consideration)
- Billing / usage metering
- The Forge → Draft PR (listed above as phase 3)

---

## 💡 Open Design Questions for Phase 2 Planning

1. **The Barracks UI** — should custom gladiators be per-user or per-instance?
   (i.e., shared across everyone using a self-hosted Colosseum, or personal)

2. **The Tribune interjection timing** — should the user be able to inject mid-turn
   (pause the current gladiator) or only between turns? Mid-turn is more dramatic
   but harder to implement cleanly with streaming.

3. **The Annals access control** — if multiple developers use the same Colosseum
   instance, are all reviews shared, or is there per-user scoping?

4. **The Forge scope** — should every confirmed issue go to the Forge, or should
   the user cherry-pick which ones to fix? Cherry-pick feels more practical for
   large reviews.

5. **GitHub App vs PAT** — GitHub App is the proper approach but requires installation
   by an org admin. PAT is simpler for self-hosted solo use. Support both?

---

## 7. 🧪 The Proving Grounds — Gladiator A/B Testing & Directive Optimisation

### Concept
The Barracks lets you build gladiators. The Proving Grounds lets you find out which
version of a gladiator actually performs best. This is prompt engineering as a
first-class feature — run controlled experiments on gladiator personas, directives,
and focus areas against real historical reviews, then let the data tell you which
variant catches more real issues.

### The Problem It Solves
A gladiator's performance is entirely determined by its prompt. "Paranoid, assumes
breach" is a personality sketch — but the actual system prompt wording, the examples
given, the focus areas listed, and the severity biases set all have meaningful impact
on whether Valeria catches a SQL injection or writes a vague non-issue. Currently
there's no way to tune this other than gut feel.

### A/B Variant System

Each gladiator in The Barracks can have multiple **Directives** — named prompt
variants that compete for the active slot:

```yaml
# barracks/valeria.gladiator
name: Valeria
domain: Security
active_directive: "v2-owasp-focused"

directives:
  v1-original:
    created: 2026-01-15
    personality: "Paranoid, assumes breach, flags every input."
    focus_areas: [input validation, auth, logging]

  v2-owasp-focused:
    created: 2026-02-10
    personality: >
      Methodical threat modeller. Maps every finding to an OWASP Top 10 category.
      Refuses to raise issues without a concrete attack vector.
    focus_areas: [OWASP Top 10, injection, auth failures, crypto failures, logging]
    severity_bias: calibrated   # vs original's "high" bias

  v3-concise:
    created: 2026-03-01
    personality: "One finding per turn, maximum specificity, no hedging."
    focus_areas: [OWASP Top 10, secrets exposure, unvalidated inputs]
```

### Running an A/B Test

From The Barracks, user selects:
- **Gladiator** to test (e.g. Valeria)
- **Variant A** and **Variant B** directives
- **Test corpus** — a set of past reviews from The Annals (e.g. last 10 sessions)
- **Metric to optimise** — see scoring system below

The system replays each historical review with both variants (using cached diffs +
transcripts, not live GitHub calls) and compares their output against the ground
truth of what the human actually marked as Fixed/Valid/Irrelevant.

This is intentionally async — test runs happen in the background, results appear
in The Proving Grounds dashboard when complete. No live debate UI needed.

### Replay architecture

```
ProvingGroundsOrchestrator:
  For each test session in corpus:
    For each variant (A, B):
      → Rebuild gladiator turns using variant directive
        (other gladiators use their current active directive)
      → Run full DebateOrchestrator with mocked other gladiators
        (to isolate the tested gladiator's contribution)
      → Collect issues raised by the tested gladiator only
      → Score against GroundTruth (from human feedback — see §8)
  → Aggregate scores across corpus
  → Produce comparison report: A vs B on each metric
```

---

## 8. 🏆 The Scoreboard — Gladiator Metrics & Human Feedback Loop

### Concept
Gladiators earn (and lose) points based on the real-world value of their findings.
This creates a feedback loop: human decisions on issues flow back into gladiator
performance scores, which inform A/B testing, which improves directives, which
improves future reviews. The whole system gets smarter over time.

### Human Issue Disposition

After a review, the user marks each issue in the Verdict Scroll with a disposition.
This is the ground truth signal that powers everything else:

| Disposition | Meaning | Points awarded |
|-------------|---------|----------------|
| ✅ **Fixed** | Issue was real, fix committed | +3 |
| 📋 **Will Fix** | Issue is real, on the backlog | +2 |
| 🙈 **Won't Fix** | Real but accepted risk / tradeoff | +1 |
| ❌ **Not Relevant** | False positive — not applicable | -1 |
| ⏭ **Skipped** | User didn't evaluate | 0 (excluded from scoring) |

Dispositions are saved to The Annals against each `IssueRecord`. They can be updated
later (e.g. "Will Fix" → "Fixed" when the PR merges).

### Debate Bonus Points

Beyond issue outcome, gladiators earn bonus points for debate behaviour:

| Behaviour | Points |
|-----------|--------|
| Raised an issue that another gladiator seconded | +1 |
| Raised an issue that became the Arbiter's top-ranked finding | +2 |
| @mentioned another gladiator and changed their stance | +1 |
| Raised a merge candidate that the Arbiter confirmed as a true duplicate | +1 |
| Raised a duplicate that added no new information | -1 |
| Asked a Tribune question that the user answered (engaged the human) | +1 |

These bonus points are computed automatically from the debate metadata already
captured in `DebateTurn`, `Issue`, and `IssueDebateEntry`.

### The Scoreboard

A persistent leaderboard per gladiator, per repo (or global), showing:

```
┌──────────────────────────────────────────────────────────────────────┐
│ THE SCOREBOARD                              repo: payments-service    │
├────────────┬──────┬────────┬──────────┬────────────┬────────────────┤
│ Gladiator  │ ELO  │ Issues │ Fixed %  │ False +ve% │ Directive      │
├────────────┼──────┼────────┼──────────┼────────────┼────────────────┤
│ Valeria    │ 1847 │  43    │  79%     │   9%       │ v2-owasp       │
│ Maximus    │ 1721 │  38    │  71%     │  14%       │ v1-original    │
│ Cassius    │ 1654 │  29    │  66%     │  18%       │ v1-original    │
│ Brutus     │ 1598 │  51    │  61%     │  22%       │ v1-original    │
└────────────┴──────┴────────┴──────────┴────────────┴────────────────┘
```

**ELO rating** is used rather than raw points to account for the difficulty of the
reviews each gladiator participated in (a finding in a complex 500-line diff is
worth more than one in a 20-line diff).

### Score visibility in the debate UI

Live scoring surfaces subtly in the Arena during debate — not as a distraction,
but as context:

- Gladiator strip chips show a small ⭐ count (career valid issues)
- Issue cards show which gladiator raised them with their current hit rate
  (e.g. "Valeria · 79% valid") — helps the Tribune prioritise which issues
  to evaluate first
- After verdict: a "This session" score panel shows each gladiator's points
  earned in the just-completed review

### The Proving Grounds dashboard

The A/B test output report for a gladiator comparison:

```
Valeria: v1-original vs v2-owasp-focused
Test corpus: 12 sessions (payments-service, last 90 days)

                    v1-original    v2-owasp-focused
Issues raised:          38             29
Fixed/Will Fix:         24 (63%)       24 (83%)    ← v2 wins
Not Relevant:            8 (21%)        3 (10%)    ← v2 wins
Avg severity:          HIGH           HIGH
Avg tokens/turn:        142            138
Tribune questions:        2              7         ← v2 asks more

Recommendation: Promote v2-owasp-focused as active directive.
Improvement: +20pp valid rate, -11pp false positive rate.
```

One-click "Promote to Active" button — updates the `.gladiator` file and takes
effect on the next session.

### Data model additions

```csharp
// New entities for The Annals
IssueDisposition       // IssueId, Disposition enum, DisposedAt, UserId (optional)
GladiatorScoreEvent    // GladiatorId, SessionId, EventType, Points, Timestamp
GladiatorStats         // GladiatorId, DirectiveId, EloRating, TotalIssues,
                       //   ValidCount, FalsePositiveCount, LastUpdated
ProvingGroundsRun      // Id, GladiatorId, VariantA, VariantB, CorpusIds,
                       //   Status, ResultJson, CreatedAt, CompletedAt
```

---

## 📦 Updated Phase 2 Build Order (incorporating §7 + §8)

```
Sprint 1 (foundations):
  [ ] Token efficiency / ContextCompressor
  [ ] Persistence layer — SQLite/EF Core (The Annals data model + new entities)
  [ ] The Annals UI — list + replay views

Sprint 2 (enrichment):
  [ ] The Barracks — .gladiator file format + directive system + UI
  [ ] Line reference extraction in IssueParser
  [ ] GitHub PR Check posting

Sprint 3 (feedback loop):
  [ ] Issue Disposition UI — Fixed/Will Fix/Won't Fix/Not Relevant on Verdict Scroll
  [ ] GladiatorScoreEvent computation — auto-score from debate metadata
  [ ] The Scoreboard UI — per-gladiator leaderboard
  [ ] Score context in Arena UI (strip chips, issue cards)

Sprint 4 (interactivity):
  [ ] The Tribune — Arena Statement + Close Issue + gladiator questions
  [ ] GitHub line-level PR comments + summary comment

Sprint 5 (The Proving Grounds):
  [ ] ProvingGroundsOrchestrator — replay engine using Annals corpus
  [ ] A/B run configuration UI
  [ ] Results dashboard + Promote to Active

Sprint 6 (The Forge):
  [ ] The Forge workflow
  [ ] Fix Ticket model + Arbiter synthesis
  [ ] Forge → GitHub Draft PR (stretch goal)
```

---

## 💡 Additional Open Design Questions (§7 + §8)

6. **Disposition timing** — should users be prompted to dispose issues immediately
   after the verdict, or is it a lazy flow (they do it whenever, maybe weeks later
   when the fix actually lands)? Lazy is more realistic but harder to drive adoption.

7. **ELO difficulty weighting** — what signals proxy for review difficulty?
   Diff size (lines changed)? Number of files? Cyclomatic complexity of changed code?
   Or just raw diff line count as a v1 approximation?

8. **A/B test isolation** — when replaying with Variant B, do the other gladiators
   also replay (full isolation) or do we use their original turns (partial isolation)?
   Full isolation is cleaner science but 4x more expensive. Partial isolation is
   cheaper but the interactions between gladiators affect what gets raised.

9. **Score gaming** — if gladiators are scored on valid issues, there's an incentive
   for a directive to raise many issues and let precision suffer. Should there be a
   diminishing returns mechanic (e.g. only top 5 issues per gladiator per session
   count toward score) to reward quality over quantity?

10. **Public Scoreboard** — should top-performing gladiator directives be shareable
    as a community library? (i.e. "install the top-rated Security gladiator from the
    community") — this is a product positioning question as much as a technical one.
