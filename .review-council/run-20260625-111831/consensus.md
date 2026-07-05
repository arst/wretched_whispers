# Consensus Code Review

Reviewers: 2 | majority threshold: 2

## C001 [SOLO/NO CONSENSUS] MEDIUM
- Claim: The Turn2 scenario run is opened before the prerequisite Turn1 setup call, so the Turn2 reporting/cache scope includes model traffic from both turns instead of only the behavior being evaluated.
- Location: WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs:[51, 57]
- Support: 1 reviewer(s): codex
- Avg confidence: 0.82
- Evidence:
  - codex: Line 51 creates the reporting configuration and line 52 opens scenario run "CampaignCreation/Turn2-Name".
  - codex: Lines 56-57 then run the prerequisite "begin" turn inside that same scenario run before executing the actual "Grim" turn on line 58.

## C003 [SOLO/NO CONSENSUS] MEDIUM
- Claim: The README/spec claim that the Evals project is "Excluded from the default CI test run" is not enforced by anything in the diff; adding the project to the solution means a solution-wide `dotnet test` now includes it.
- Location: WrtechedWhispers/WretchedWhispers.Evals/README.md:[3, 4]
- Support: 1 reviewer(s): claude
- Avg confidence: 0.6
- Evidence:
  - claude: README.md: "Unlike the unit tests ... Excluded from the default CI test run."
  - claude: WrtechedWhispers.sln adds project {E467EB98-...} so `dotnet test WrtechedWhispers/WrtechedWhispers.sln` will discover it

## C004 [SOLO/NO CONSENSUS] MEDIUM
- Claim: The assistant turn is persisted with only the narrative text; the tool calls (and any tool results) are not written to chat history, so multi-turn evals reconstruct a history shape that may differ from production TurnCoordinator behavior.
- Location: WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs:[45, 48]
- Support: 1 reviewer(s): claude
- Avg confidence: 0.55
- Evidence:
  - claude: EvalTurnRunner.cs: `new ChatMessage(ChatRole.Assistant, narrative.ToString()) { AuthorName = "Game_Master" }` — no FunctionCallContent/FunctionResultContent saved
  - claude: Class doc: "Mirrors TurnCoordinator's per-turn steps minus the transaction/SSE layer" — drift from TurnCoordinator is explicitly a concern the design calls out

## C007 [SOLO/NO CONSENSUS] LOW
- Claim: When the scripted queue is exhausted the client silently returns an empty assistant message instead of failing, so a test that triggers more model round-trips than scripted can pass spuriously with empty output.
- Location: WrtechedWhispers/WretchedWhispers.Evals/Harness/ScriptedChatClient.cs:[12, 17]
- Support: 1 reviewer(s): claude
- Avg confidence: 0.7
- Evidence:
  - claude: ScriptedChatClient.cs: `_responses.Count > 0 ? _responses.Dequeue() : new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty))`

## C002 [SOLO/NO CONSENSUS] LOW
- Claim: Both live eval tests use the same disk storage path and executionName, which can collide if xUnit runs the tests in parallel.
- Location: WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs:[78, 84]
- Support: 1 reviewer(s): codex
- Avg confidence: 0.67
- Evidence:
  - codex: Line 80 sets a fixed storageRootPath under AppContext.BaseDirectory/.eval-results.
  - codex: Line 84 sets the same executionName "campaign-creation" for every test.

## C006 [SOLO/NO CONSENSUS] LOW
- Claim: The IChatClient created from AzureOpenAIClient is never disposed by the caller; EvalHost takes the (cached) client but explicitly does not own/dispose the underlying Azure client.
- Location: WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs:[99, 109]
- Support: 1 reviewer(s): claude
- Avg confidence: 0.6
- Evidence:
  - claude: CampaignCreationEvals.cs: `return azure.GetChatClient(deployment).AsIChatClient();` — no using/dispose at any call site
  - claude: EvalHost stores `_chatClient` but DisposeAsync only disposes the provider and SQLite connection

## C008 [SOLO/NO CONSENSUS] LOW
- Claim: Response caching is enabled without explicit cachingKeys, so cache invalidation relies solely on the request contents matching; a change that should bust the cache but isn't reflected in the serialized request could replay a stale response.
- Location: WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs:[86, 97]
- Support: 1 reviewer(s): claude
- Avg confidence: 0.4
- Evidence:
  - claude: CreateReportingConfiguration: `enableResponseCaching: true, timeToLiveForCacheEntries: null, executionName: "campaign-creation"` — cachingKeys omitted (the comment documents it as part of the signature)

## C005 [SOLO/NO CONSENSUS] LOW
- Claim: If AgentExecutor.ExecuteAsync already persists the user/assistant turn to IChatHistoryRepository internally, the runner's explicit SaveMessage calls would duplicate those messages in history.
- Location: WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs:[32, 48]
- Support: 1 reviewer(s): claude
- Avg confidence: 0.3
- Evidence:
  - claude: EvalTurnRunner.cs: explicit `SaveMessage(chatSessionId, ChatRole.User, ...)` before `agentExecutor.ExecuteAsync(...)` and `SaveMessage(... ChatRole.Assistant ...)` after
  - claude: AgentExecutor is constructed with the same `chatRepo` (EvalHost.CreateTurnRunner), so it has the means to persist as well

# Ambiguous pairs for optional LLM judge
- codex:2 vs claude:4 score=0.498 reasons=same_file, near_lines, different_kind, low_claim_similarity:0.24
- codex:2 vs claude:6 score=0.606 reasons=same_file, near_lines, same_symbol, different_kind, low_claim_similarity:0.03
