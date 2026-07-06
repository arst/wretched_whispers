Act as an independent code reviewer named codex.
Review only the provided diff and context. Do not infer or mention other reviewers' opinions.
Return JSON only using the provided finding schema. Do not wrap it in Markdown.
Every defect must cite repository-relative file and line evidence when possible.
Separate confirmed defects from risks and suggestions.

Required JSON schema:
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "Code review findings",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "reviewer": {
      "type": "string",
      "description": "Reviewer name, e.g. codex, claude, or gemini."
    },
    "findings": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "kind": {
            "type": "string",
            "enum": [
              "defect",
              "risk",
              "suggestion"
            ]
          },
          "category": {
            "type": "string"
          },
          "bug_pattern": {
            "anyOf": [
              {
                "type": "string"
              },
              {
                "type": "null"
              }
            ]
          },
          "severity": {
            "type": "string",
            "enum": [
              "critical",
              "high",
              "medium",
              "low",
              "info"
            ]
          },
          "confidence": {
            "type": "number",
            "minimum": 0,
            "maximum": 1
          },
          "file": {
            "anyOf": [
              {
                "type": "string"
              },
              {
                "type": "null"
              }
            ]
          },
          "line_range": {
            "anyOf": [
              {
                "type": "array",
                "minItems": 2,
                "maxItems": 2,
                "items": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              {
                "type": "null"
              }
            ]
          },
          "affected_symbol": {
            "anyOf": [
              {
                "type": "string"
              },
              {
                "type": "null"
              }
            ]
          },
          "claim": {
            "type": "string"
          },
          "evidence": {
            "type": "array",
            "items": {
              "type": "string"
            }
          },
          "suggested_fix": {
            "anyOf": [
              {
                "type": "string"
              },
              {
                "type": "null"
              }
            ]
          },
          "rationale": {
            "type": "string"
          }
        },
        "required": [
          "kind",
          "category",
          "bug_pattern",
          "severity",
          "confidence",
          "file",
          "line_range",
          "affected_symbol",
          "claim",
          "evidence",
          "suggested_fix",
          "rationale"
        ]
      }
    }
  },
  "required": [
    "reviewer",
    "findings"
  ]
}

```

Review rubric:
Focus on concrete, actionable review findings:
- correctness bugs and edge cases
- security, authorization, and tenant isolation problems
- data integrity, data loss, and migration risks
- concurrency, idempotency, and race conditions
- performance regressions with code evidence
- API compatibility and contract breaks
- missing or misleading tests when they materially affect confidence

Avoid generic style comments. Do not duplicate findings. Use kind=risk when the issue is plausible but not proven from the diff. Use kind=suggestion for improvements that are not defects.

Repository: /home/arst/Projects/wretched_whispers
Review target: branch diff: main...HEAD
Generated at: 2026-06-25T08:40:40.210432+00:00

Git status:
```text
?? .review-council/
```

Changed files:
```text
A	WrtechedWhispers/WretchedWhispers.Evals/.gitignore
A	WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs
A	WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ExpectedToolCallOrderContext.cs
A	WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluator.cs
A	WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluatorTests.cs
A	WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalHost.cs
A	WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs
A	WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunnerTests.cs
A	WrtechedWhispers/WretchedWhispers.Evals/Harness/ScriptedChatClient.cs
A	WrtechedWhispers/WretchedWhispers.Evals/README.md
A	WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
M	WrtechedWhispers/WrtechedWhispers.sln
A	docs/superpowers/plans/2026-06-09-campaign-creation-eval.md
A	docs/superpowers/specs/2026-06-09-campaign-creation-eval-design.md
```

Diff to review:
```diff
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/.gitignore b/WrtechedWhispers/WretchedWhispers.Evals/.gitignore
new file mode 100644
index 0000000..32736ba
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/.gitignore
@@ -0,0 +1,2 @@
+# Local-only eval response cache + stored results (not a committed CI artifact)
+.eval-results/
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs b/WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs
new file mode 100644
index 0000000..afbf90e
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs
@@ -0,0 +1,109 @@
+using System.ClientModel;
+using Azure.AI.OpenAI;
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.AI.Evaluation;
+using Microsoft.Extensions.AI.Evaluation.Reporting;
+using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
+using WretchedWhispers.Evals.Evaluators;
+using WretchedWhispers.Evals.Harness;
+using Xunit;
+
+namespace WretchedWhispers.Evals;
+
+public class CampaignCreationEvals
+{
+    private static readonly string[] CreateCampaignTools =
+        ["CreateCharacter", "ConfigureCampaign", "StartCampaign"];
+
+    [Fact]
+    public async Task Turn1_Begin_CallsNoTools()
+    {
+        var chatClient = TryCreateAzureChatClient();
+        if (chatClient is null)
+        {
+            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
+            return;
+        }
+
+        var reporting = CreateReportingConfiguration(chatClient);
+        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation/Turn1-Begin");
+
+        var chatConfiguration = run.ChatConfiguration
+            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
+        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);
+        var outcome = await host.CreateTurnRunner().RunTurnAsync("begin");
+
+        EvaluationResult result = await run.EvaluateAsync(
+            messages: [],
+            modelResponse: outcome.Response,
+            additionalContext: [new ExpectedToolCallOrderContext([])]);
+
+        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
+        Assert.True(metric.Value, $"Expected no tools on 'begin'; got [{string.Join(", ", outcome.ToolCalls)}]");
+    }
+
+    [Fact]
+    public async Task Turn2_Name_CreatesCampaignInOrder()
+    {
+        var chatClient = TryCreateAzureChatClient();
+        if (chatClient is null)
+        {
+            Assert.Skip("Azure OpenAI credentials not configured; skipping live eval.");
+            return;
+        }
+
+        var reporting = CreateReportingConfiguration(chatClient);
+        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation/Turn2-Name");
+
+        var chatConfiguration = run.ChatConfiguration
+            ?? throw new InvalidOperationException("ScenarioRun has no ChatConfiguration; response caching was not wired.");
+        await using var host = await EvalHost.CreateAsync(chatConfiguration.ChatClient);
+
+        // Turn 1 first so the model has asked for a name and history is consistent.
+        await host.CreateTurnRunner().RunTurnAsync("begin");
+        var outcome = await host.CreateTurnRunner().RunTurnAsync("Grim");
+
+        EvaluationResult result = await run.EvaluateAsync(
+            messages: [],
+            modelResponse: outcome.Response,
+            additionalContext: [new ExpectedToolCallOrderContext(CreateCampaignTools)]);
+
+        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
+        Assert.True(metric.Value,
+            $"Expected [{string.Join(", ", CreateCampaignTools)}]; got [{string.Join(", ", outcome.ToolCalls)}]");
+    }
+
+    // DiskBasedReportingConfiguration.Create signature (verified against 10.6.0):
+    //   Create(storageRootPath, evaluators, chatConfiguration, enableResponseCaching,
+    //          timeToLiveForCacheEntries, cachingKeys, executionName,
+    //          evaluationMetricInterpreter, tags)
+    // The plan omitted timeToLiveForCacheEntries (nullable TimeSpan) — we pass null to use the default.
+    // ChatConfiguration is not IAsyncDisposable; no await using needed.
+    private static ReportingConfiguration CreateReportingConfiguration(IChatClient chatClient)
+    {
+        var chatConfiguration = new ChatConfiguration(chatClient);
+        return DiskBasedReportingConfiguration.Create(
+            storageRootPath: Path.Combine(AppContext.BaseDirectory, ".eval-results"),
+            evaluators: [new ToolCallOrderEvaluator()],
+            chatConfiguration: chatConfiguration,
+            enableResponseCaching: true,
+            timeToLiveForCacheEntries: null,
+            executionName: "campaign-creation");
+    }
+
+    // Mirrors AgentConfiguration's Azure wiring. Configuration section AzureOpenAiSettings binds
+    // from env vars AzureOpenAiSettings__Endpoint, AzureOpenAiSettings__ApiKey, and
+    // AzureOpenAiSettings__ChatModelDeployment (ASP.NET Core double-underscore convention).
+    // Returns null if any are absent so the [Fact] skips cleanly.
+    private static IChatClient? TryCreateAzureChatClient()
+    {
+        var endpoint = Environment.GetEnvironmentVariable("AzureOpenAiSettings__Endpoint");
+        var apiKey = Environment.GetEnvironmentVariable("AzureOpenAiSettings__ApiKey");
+        var deployment = Environment.GetEnvironmentVariable("AzureOpenAiSettings__ChatModelDeployment");
+        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(deployment))
+            return null;
+
+        var azure = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
+        return azure.GetChatClient(deployment).AsIChatClient();
+    }
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ExpectedToolCallOrderContext.cs b/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ExpectedToolCallOrderContext.cs
new file mode 100644
index 0000000..5213d75
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ExpectedToolCallOrderContext.cs
@@ -0,0 +1,22 @@
+using Microsoft.Extensions.AI.Evaluation;
+
+namespace WretchedWhispers.Evals.Evaluators;
+
+/// <summary>
+/// Carries the expected ordered tool-call names into <see cref="ToolCallOrderEvaluator"/> for a single
+/// scenario run. The strongly-typed <see cref="Expected"/> list is what the evaluator reads; the base
+/// name/content are for human-readable reporting.
+/// </summary>
+public sealed class ExpectedToolCallOrderContext : EvaluationContext
+{
+    public IReadOnlyList<string> Expected { get; }
+
+    // EvaluationContext base ctor: protected(string name, string content)
+    public ExpectedToolCallOrderContext(IReadOnlyList<string> expected)
+        : base(
+            name: "Expected Tool Call Order",
+            content: expected.Count == 0 ? "(no tools)" : string.Join(" -> ", expected))
+    {
+        Expected = expected;
+    }
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluator.cs b/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluator.cs
new file mode 100644
index 0000000..f1e54b9
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluator.cs
@@ -0,0 +1,48 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.AI.Evaluation;
+
+namespace WretchedWhispers.Evals.Evaluators;
+
+/// <summary>
+/// Deterministic, non-AI evaluator: checks that the tool calls in a <see cref="ChatResponse"/> match an
+/// expected ordered sequence EXACTLY (same tools, same order, no extras). Expected order is supplied via
+/// an <see cref="ExpectedToolCallOrderContext"/> in <c>additionalContext</c>; actual order is read from
+/// the response's <see cref="FunctionCallContent"/>s.
+/// </summary>
+public sealed class ToolCallOrderEvaluator : IEvaluator
+{
+    public const string MetricName = "Tool Call Order";
+
+    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];
+
+    public ValueTask<EvaluationResult> EvaluateAsync(
+        IEnumerable<ChatMessage> messages,
+        ChatResponse modelResponse,
+        ChatConfiguration? chatConfiguration = null,
+        IEnumerable<EvaluationContext>? additionalContext = null,
+        CancellationToken cancellationToken = default)
+    {
+        if (additionalContext?.OfType<ExpectedToolCallOrderContext>().FirstOrDefault() is not { } context)
+        {
+            // No context provided — report an error metric with Value = null (indeterminate)
+            var errMetric = new BooleanMetric(MetricName, null, null);
+            errMetric.Diagnostics = [EvaluationDiagnostic.Error(
+                $"No {nameof(ExpectedToolCallOrderContext)} was supplied in {nameof(additionalContext)}.")];
+            return new ValueTask<EvaluationResult>(new EvaluationResult(errMetric));
+        }
+
+        List<string> actual = modelResponse.Messages
+            .SelectMany(m => m.Contents ?? [])
+            .OfType<FunctionCallContent>()
+            .Select(c => c.Name)
+            .ToList();
+
+        bool passed = actual.SequenceEqual(context.Expected, StringComparer.Ordinal);
+
+        var metric = new BooleanMetric(MetricName, passed, null);
+        metric.Diagnostics = [EvaluationDiagnostic.Informational(
+            $"expected: [{string.Join(", ", context.Expected)}]; actual: [{string.Join(", ", actual)}]")];
+
+        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
+    }
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluatorTests.cs b/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluatorTests.cs
new file mode 100644
index 0000000..d772dca
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluatorTests.cs
@@ -0,0 +1,89 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.AI.Evaluation;
+using Xunit;
+
+namespace WretchedWhispers.Evals.Evaluators;
+
+public class ToolCallOrderEvaluatorTests
+{
+    private static ChatResponse ResponseWithToolCalls(params string[] names)
+    {
+        var contents = names
+            .Select((n, i) => (AIContent)new FunctionCallContent($"call_{i}", n))
+            .ToList();
+        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
+    }
+
+    private static async Task<BooleanMetric> EvaluateAsync(ChatResponse response, IReadOnlyList<string> expected)
+    {
+        var evaluator = new ToolCallOrderEvaluator();
+        var context = new ExpectedToolCallOrderContext(expected);
+        EvaluationResult result = await evaluator.EvaluateAsync(
+            messages: [],
+            modelResponse: response,
+            additionalContext: [context]);
+
+        return result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
+    }
+
+    [Fact]
+    public async Task ExactOrderMatch_Passes()
+    {
+        var response = ResponseWithToolCalls("CreateCharacter", "ConfigureCampaign", "StartCampaign");
+        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
+        Assert.True(metric.Value);
+    }
+
+    [Fact]
+    public async Task WrongOrder_Fails()
+    {
+        var response = ResponseWithToolCalls("ConfigureCampaign", "CreateCharacter", "StartCampaign");
+        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
+        Assert.False(metric.Value);
+    }
+
+    [Fact]
+    public async Task MissingTool_Fails()
+    {
+        var response = ResponseWithToolCalls("CreateCharacter", "ConfigureCampaign");
+        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
+        Assert.False(metric.Value);
+    }
+
+    [Fact]
+    public async Task NoToolsExpected_NoneCalled_Passes()
+    {
+        var response = ResponseWithToolCalls(); // none
+        var metric = await EvaluateAsync(response, []);
+        Assert.True(metric.Value);
+    }
+
+    [Fact]
+    public async Task NoToolsExpected_ButOneCalled_Fails()
+    {
+        var response = ResponseWithToolCalls("CreateCharacter");
+        var metric = await EvaluateAsync(response, []);
+        Assert.False(metric.Value);
+    }
+
+    [Fact]
+    public async Task MultiMessage_WithText_ReadsToolCallsInOrder_IgnoringText()
+    {
+        var response = new ChatResponse(new List<ChatMessage>
+        {
+            new(ChatRole.Assistant, new List<AIContent>
+            {
+                new TextContent("Working on it..."),
+                new FunctionCallContent("call_0", "CreateCharacter")
+            }),
+            new(ChatRole.Assistant, new List<AIContent>
+            {
+                new FunctionCallContent("call_1", "ConfigureCampaign"),
+                new TextContent("done")
+            })
+        });
+
+        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign"]);
+        Assert.True(metric.Value);
+    }
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalHost.cs b/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalHost.cs
new file mode 100644
index 0000000..8f8fbf3
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalHost.cs
@@ -0,0 +1,127 @@
+using Microsoft.Data.Sqlite;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging.Abstractions;
+using WretchedWhispers.Api.Services;
+using WretchedWhispers.Core;
+using WretchedWhispers.Core.Campaigns;
+using WretchedWhispers.Core.Characters;
+using WretchedWhispers.Core.Dices;
+using WretchedWhispers.Core.Encounters;
+using WretchedWhispers.Infrastructure;
+using WretchedWhispers.Infrastructure.Persistence;
+
+namespace WretchedWhispers.Evals.Harness;
+
+/// <summary>
+/// Builds production wiring (real Core services + tools) over an in-memory SQLite database for an eval
+/// run, seeded with an empty campaign + chat session so turns start in the CharacterCreation stage. The
+/// supplied chat client is the one the agent will call.
+/// </summary>
+public sealed class EvalHost : IAsyncDisposable
+{
+    private const string TestUserId = "eval-user";
+
+    private readonly SqliteConnection _connection;
+    private readonly ServiceProvider _provider;
+    private readonly IChatClient _chatClient;
+    private readonly List<EvalTurnRunner> _runners = new();
+
+    public Guid SessionId { get; }
+    public Guid ChatSessionId { get; }
+
+    private EvalHost(SqliteConnection connection, ServiceProvider provider, IChatClient chatClient, Guid sessionId, Guid chatSessionId)
+    {
+        _connection = connection;
+        _provider = provider;
+        _chatClient = chatClient;
+        SessionId = sessionId;
+        ChatSessionId = chatSessionId;
+    }
+
+    public static async Task<EvalHost> CreateAsync(IChatClient chatClient)
+    {
+        var connection = new SqliteConnection("DataSource=:memory:");
+        connection.Open();
+
+        var services = new ServiceCollection();
+        services.AddDbContext<WretchedWhispersDbContext>(o => o.UseSqlite(connection));
+        services.AddDomainServices();
+
+        var provider = services.BuildServiceProvider();
+
+        // Ensure schema exists using a short-lived scope.
+        await using (var scope = provider.CreateAsyncScope())
+        {
+            var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
+            await db.Database.EnsureCreatedAsync();
+        }
+
+        // Seed an empty campaign + chat session so the first turn starts in CharacterCreation stage.
+        Guid sessionId;
+        Guid chatSessionId;
+        await using (var scope = provider.CreateAsyncScope())
+        {
+            var sp = scope.ServiceProvider;
+            SetTenantUser(sp);
+
+            var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
+            var chatRepo = sp.GetRequiredService<IChatHistoryRepository>();
+
+            var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Eval Campaign", "A doomed eval run");
+            await campaignsRepo.SaveCampaign(campaign, TestUserId);
+            chatSessionId = await chatRepo.CreateSession(campaign.Id);
+
+            sessionId = campaign.Id;
+        }
+
+        return new EvalHost(connection, provider, chatClient, sessionId, chatSessionId);
+    }
+
+    /// <summary>
+    /// Creates a new <see cref="EvalTurnRunner"/> scoped to one eval turn and registers it for
+    /// disposal. The runner is owned by this host and will be disposed when the host is disposed —
+    /// callers must NOT dispose the runner themselves.
+    /// </summary>
+    public EvalTurnRunner CreateTurnRunner()
+    {
+        var scope = _provider.CreateAsyncScope();
+        var sp = scope.ServiceProvider;
+        SetTenantUser(sp);
+
+        var contextLoader = new SessionContextLoader(
+            sp.GetRequiredService<ICampaignsRepository>(),
+            sp.GetRequiredService<ICharactersRepository>(),
+            sp.GetRequiredService<IEncountersRepository>(),
+            NullLogger<SessionContextLoader>.Instance);
+
+        var toolProvider = new AgentToolProvider(sp, NullLogger<AgentToolProvider>.Instance);
+
+        var chatRepo = sp.GetRequiredService<IChatHistoryRepository>();
+        var executor = new AgentExecutor(
+            _chatClient,
+            chatRepo,
+            new ChatHistoryReducer(_chatClient, NullLogger<ChatHistoryReducer>.Instance),
+            new PromptComposer(),
+            NullLogger<AgentExecutor>.Instance);
+
+        var runner = new EvalTurnRunner(scope, contextLoader, toolProvider, executor, chatRepo, SessionId, ChatSessionId);
+        _runners.Add(runner);
+        return runner;
+    }
+
+    public async ValueTask DisposeAsync()
+    {
+        // Dispose runners (and their scopes) first, then the root provider, then the connection.
+        foreach (var runner in _runners)
+            await runner.DisposeAsync();
+
+        await _provider.DisposeAsync();
+        _connection.Close();
+        _connection.Dispose();
+    }
+
+    private static void SetTenantUser(IServiceProvider sp) =>
+        ((TenantContext)sp.GetRequiredService<ITenantContext>()).SetUserId(TestUserId);
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs b/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs
new file mode 100644
index 0000000..9ef7f13
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs
@@ -0,0 +1,65 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.DependencyInjection;
+using WretchedWhispers.Api.Models;
+using WretchedWhispers.Api.Services;
+using WretchedWhispers.Infrastructure.Persistence;
+
+namespace WretchedWhispers.Evals.Harness;
+
+/// <summary>The captured result of one eval turn: the ordered tool-call names, plus a ChatResponse that
+/// packages those calls as FunctionCallContent for the evaluator.</summary>
+public sealed record TurnOutcome(IReadOnlyList<string> ToolCalls, ChatResponse Response, string Narrative);
+
+/// <summary>
+/// Runs one CharacterCreation turn through the real AgentExecutor and captures the tool calls. Mirrors
+/// TurnCoordinator's per-turn steps minus the transaction/SSE layer.
+/// </summary>
+public sealed class EvalTurnRunner(
+    AsyncServiceScope scope,
+    ISessionContextLoader contextLoader,
+    IAgentToolProvider toolProvider,
+    IAgentExecutor agentExecutor,
+    IChatHistoryRepository chatHistoryRepository,
+    Guid sessionId,
+    Guid chatSessionId) : IAsyncDisposable
+{
+    public async Task<TurnOutcome> RunTurnAsync(string playerMessage, CancellationToken ct = default)
+    {
+        var context = await contextLoader.LoadAsync(sessionId, ct);
+        var stage = context.DeriveStage();
+        var (tools, _) = toolProvider.GetToolsForStage(context, stage);
+
+        await chatHistoryRepository.SaveMessage(chatSessionId, new ChatMessage(ChatRole.User, playerMessage), ct);
+
+        var toolCalls = new List<string>();
+        var narrative = new System.Text.StringBuilder();
+
+        await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
+        {
+            if (evt is ToolResult tr)
+                toolCalls.Add(tr.Function);
+            else if (evt is NarrativeChunk chunk)
+                narrative.Append(chunk.Text);
+        }
+
+        await chatHistoryRepository.SaveMessage(
+            chatSessionId,
+            new ChatMessage(ChatRole.Assistant, narrative.ToString()) { AuthorName = "Game_Master" },
+            ct);
+
+        var response = BuildToolCallResponse(toolCalls, narrative.ToString());
+        return new TurnOutcome(toolCalls, response, narrative.ToString());
+    }
+
+    private static ChatResponse BuildToolCallResponse(IReadOnlyList<string> toolCalls, string narrative)
+    {
+        var contents = new List<AIContent>();
+        for (int i = 0; i < toolCalls.Count; i++)
+            contents.Add(new FunctionCallContent($"call_{i}", toolCalls[i]));
+        if (!string.IsNullOrEmpty(narrative))
+            contents.Add(new TextContent(narrative));
+        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
+    }
+
+    public ValueTask DisposeAsync() => scope.DisposeAsync();
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunnerTests.cs b/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunnerTests.cs
new file mode 100644
index 0000000..53088fa
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunnerTests.cs
@@ -0,0 +1,51 @@
+using Microsoft.Extensions.AI;
+using Xunit;
+
+namespace WretchedWhispers.Evals.Harness;
+
+public class EvalTurnRunnerTests
+{
+    [Fact]
+    public async Task RunTurn_CapturesToolCallOrder_FromExecutorEvents()
+    {
+        // The scripted model: first call requests CreateCharacter, second call narrates the result.
+        var client = new ScriptedChatClient(
+            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
+            {
+                new FunctionCallContent("call_1", "CreateCharacter", new Dictionary<string, object?> { ["name"] = "Grim" })
+            })),
+            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grim claws free of the muck.")));
+
+        await using var host = await EvalHost.CreateAsync(client);
+        var runner = host.CreateTurnRunner();
+
+        var outcome = await runner.RunTurnAsync("Grim");
+
+        Assert.Equal(new[] { "CreateCharacter" }, outcome.ToolCalls);
+        var packagedNames = outcome.Response.Messages
+            .SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.Name);
+        Assert.Equal(new[] { "CreateCharacter" }, packagedNames);
+    }
+
+    [Fact]
+    public async Task TwoTurns_ShareState_AcrossSeparateScopes()
+    {
+        // Turn 1: the model asks for a name, no tools (1 response).
+        // Turn 2: the model creates the character (2 responses: tool-call + narration).
+        var client = new ScriptedChatClient(
+            new ChatResponse(new ChatMessage(ChatRole.Assistant, "What name is carved into your hide?")),
+            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
+            {
+                new FunctionCallContent("call_1", "CreateCharacter", new Dictionary<string, object?> { ["name"] = "Grim" })
+            })),
+            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grim claws free of the muck.")));
+
+        await using var host = await EvalHost.CreateAsync(client);
+
+        var turn1 = await host.CreateTurnRunner().RunTurnAsync("begin");
+        Assert.Empty(turn1.ToolCalls);
+
+        var turn2 = await host.CreateTurnRunner().RunTurnAsync("Grim");
+        Assert.Equal(new[] { "CreateCharacter" }, turn2.ToolCalls);
+    }
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/Harness/ScriptedChatClient.cs b/WrtechedWhispers/WretchedWhispers.Evals/Harness/ScriptedChatClient.cs
new file mode 100644
index 0000000..ea32771
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/Harness/ScriptedChatClient.cs
@@ -0,0 +1,31 @@
+using System.Runtime.CompilerServices;
+using Microsoft.Extensions.AI;
+
+namespace WretchedWhispers.Evals.Harness;
+
+/// <summary>Replays a fixed queue of ChatResponses — used only to test the harness plumbing without a real model.</summary>
+public sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
+{
+    private readonly Queue<ChatResponse> _responses = new(responses);
+
+    public Task<ChatResponse> GetResponseAsync(
+        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
+    {
+        var response = _responses.Count > 0
+            ? _responses.Dequeue()
+            : new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty));
+        return Task.FromResult(response);
+    }
+
+    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
+        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
+        [EnumeratorCancellation] CancellationToken cancellationToken = default)
+    {
+        var response = await GetResponseAsync(messages, options, cancellationToken);
+        foreach (var message in response.Messages)
+            yield return new ChatResponseUpdate(message.Role, message.Contents);
+    }
+
+    public object? GetService(Type serviceType, object? serviceKey = null) => null;
+    public void Dispose() { }
+}
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/README.md b/WrtechedWhispers/WretchedWhispers.Evals/README.md
new file mode 100644
index 0000000..7322957
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/README.md
@@ -0,0 +1,35 @@
+# WretchedWhispers.Evals
+
+Behavioral evals for the AI game master, built on `Microsoft.Extensions.AI.Evaluation`. Unlike the unit
+tests (scripted, deterministic), these drive the **real** model and **score** behavior. Excluded from the
+default CI test run.
+
+## Running
+
+Set the Azure OpenAI credentials the app uses (see `AgentConfiguration`):
+
+```bash
+export AzureOpenAiSettings__Endpoint="https://<resource>.openai.azure.com/"
+export AzureOpenAiSettings__ApiKey="<key>"
+export AzureOpenAiSettings__ChatModelDeployment="<deployment>"
+
+dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
+```
+
+Without these set, the evals **skip**. The first run hits Azure and writes a response cache under
+`.eval-results/` (git-ignored); later runs replay from the cache and are free + deterministic.
+
+## Reporting
+
+Results are stored under `.eval-results/` relative to the test binary output directory. Render an HTML
+report with the eval console tool:
+
+```bash
+dotnet tool install --global Microsoft.Extensions.AI.Evaluation.Console
+dotnet aieval report --path WrtechedWhispers/WretchedWhispers.Evals/bin/Debug/net10.0/.eval-results --output eval-report.html
+```
+
+## Current evals
+
+- **CampaignCreation/Turn1-Begin** — "begin" must call no tools (asks for a name).
+- **CampaignCreation/Turn2-Name** — a name must trigger `CreateCharacter -> ConfigureCampaign -> StartCampaign`, in that exact order.
diff --git a/WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj b/WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
new file mode 100644
index 0000000..77aa301
--- /dev/null
+++ b/WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
@@ -0,0 +1,37 @@
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <PropertyGroup>
+    <TargetFramework>net10.0</TargetFramework>
+    <Nullable>enable</Nullable>
+    <ImplicitUsings>enable</ImplicitUsings>
+    <IsPackable>false</IsPackable>
+    <IsTestProject>true</IsTestProject>
+  </PropertyGroup>
+
+  <ItemGroup>
+    <PackageReference Include="Microsoft.Extensions.AI.Evaluation">
+      <Version>10.6.0</Version>
+    </PackageReference>
+    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting">
+      <Version>10.6.0</Version>
+    </PackageReference>
+    <PackageReference Include="Microsoft.NET.Test.Sdk">
+      <Version>18.6.0</Version>
+    </PackageReference>
+    <PackageReference Include="xunit.v3">
+      <Version>3.2.2</Version>
+    </PackageReference>
+    <PackageReference Include="xunit.runner.visualstudio">
+      <Version>3.1.5</Version>
+      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
+      <PrivateAssets>all</PrivateAssets>
+    </PackageReference>
+  </ItemGroup>
+
+  <ItemGroup>
+    <ProjectReference Include="..\WretchedWhispers.Api\WretchedWhispers.Api.csproj" />
+    <ProjectReference Include="..\WretchedWhispers.Infrastructure\WretchedWhispers.Infrastructure.csproj" />
+    <ProjectReference Include="..\WretchedWhispers.Core\WretchedWhispers.Core.csproj" />
+  </ItemGroup>
+
+</Project>
diff --git a/WrtechedWhispers/WrtechedWhispers.sln b/WrtechedWhispers/WrtechedWhispers.sln
index 4fa6a4e..0323fcf 100644
--- a/WrtechedWhispers/WrtechedWhispers.sln
+++ b/WrtechedWhispers/WrtechedWhispers.sln
@@ -1,73 +1,87 @@
 ﻿
 Microsoft Visual Studio Solution File, Format Version 12.00
 Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "WretchedWhispers.Core", "WretchedWhispers.Core\WretchedWhispers.Core.csproj", "{245B5F70-2800-4AAD-B671-EC007FD97401}"
 EndProject
 Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "WretchedWhispers.Infrastructure", "WretchedWhispers.Infrastructure\WretchedWhispers.Infrastructure.csproj", "{20176251-4671-4CA1-BC0E-928DBA9155B7}"
 EndProject
 Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "WretchedWhispers.Tests", "WretchedWhispers.Tests\WretchedWhispers.Tests.csproj", "{3155FF4D-C4A5-4763-B202-C83B937E6D08}"
 EndProject
 Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "WretchedWhispers.Api", "WretchedWhispers.Api\WretchedWhispers.Api.csproj", "{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}"
 EndProject
+Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "WretchedWhispers.Evals", "WretchedWhispers.Evals\WretchedWhispers.Evals.csproj", "{E467EB98-B153-465C-A91A-57A164A65833}"
+EndProject
 Global
 	GlobalSection(SolutionConfigurationPlatforms) = preSolution
 		Debug|Any CPU = Debug|Any CPU
 		Debug|x64 = Debug|x64
 		Debug|x86 = Debug|x86
 		Release|Any CPU = Release|Any CPU
 		Release|x64 = Release|x64
 		Release|x86 = Release|x86
 	EndGlobalSection
 	GlobalSection(ProjectConfigurationPlatforms) = postSolution
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Debug|Any CPU.Build.0 = Debug|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Debug|x64.ActiveCfg = Debug|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Debug|x64.Build.0 = Debug|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Debug|x86.ActiveCfg = Debug|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Debug|x86.Build.0 = Debug|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Release|Any CPU.ActiveCfg = Release|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Release|Any CPU.Build.0 = Release|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Release|x64.ActiveCfg = Release|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Release|x64.Build.0 = Release|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Release|x86.ActiveCfg = Release|Any CPU
 		{245B5F70-2800-4AAD-B671-EC007FD97401}.Release|x86.Build.0 = Release|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Debug|Any CPU.Build.0 = Debug|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Debug|x64.ActiveCfg = Debug|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Debug|x64.Build.0 = Debug|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Debug|x86.ActiveCfg = Debug|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Debug|x86.Build.0 = Debug|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Release|Any CPU.ActiveCfg = Release|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Release|Any CPU.Build.0 = Release|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Release|x64.ActiveCfg = Release|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Release|x64.Build.0 = Release|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Release|x86.ActiveCfg = Release|Any CPU
 		{20176251-4671-4CA1-BC0E-928DBA9155B7}.Release|x86.Build.0 = Release|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Debug|Any CPU.Build.0 = Debug|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Debug|x64.ActiveCfg = Debug|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Debug|x64.Build.0 = Debug|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Debug|x86.ActiveCfg = Debug|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Debug|x86.Build.0 = Debug|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Release|Any CPU.ActiveCfg = Release|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Release|Any CPU.Build.0 = Release|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Release|x64.ActiveCfg = Release|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Release|x64.Build.0 = Release|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Release|x86.ActiveCfg = Release|Any CPU
 		{3155FF4D-C4A5-4763-B202-C83B937E6D08}.Release|x86.Build.0 = Release|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Debug|Any CPU.Build.0 = Debug|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Debug|x64.ActiveCfg = Debug|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Debug|x64.Build.0 = Debug|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Debug|x86.ActiveCfg = Debug|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Debug|x86.Build.0 = Debug|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Release|Any CPU.ActiveCfg = Release|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Release|Any CPU.Build.0 = Release|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Release|x64.ActiveCfg = Release|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Release|x64.Build.0 = Release|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Release|x86.ActiveCfg = Release|Any CPU
 		{B9A036CC-4AC2-45B3-B316-50D1E49EEDEE}.Release|x86.Build.0 = Release|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Debug|Any CPU.Build.0 = Debug|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Debug|x64.ActiveCfg = Debug|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Debug|x64.Build.0 = Debug|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Debug|x86.ActiveCfg = Debug|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Debug|x86.Build.0 = Debug|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Release|Any CPU.ActiveCfg = Release|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Release|Any CPU.Build.0 = Release|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Release|x64.ActiveCfg = Release|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Release|x64.Build.0 = Release|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Release|x86.ActiveCfg = Release|Any CPU
+		{E467EB98-B153-465C-A91A-57A164A65833}.Release|x86.Build.0 = Release|Any CPU
 	EndGlobalSection
 	GlobalSection(SolutionProperties) = preSolution
 		HideSolutionNode = FALSE
 	EndGlobalSection
 EndGlobal
diff --git a/docs/superpowers/plans/2026-06-09-campaign-creation-eval.md b/docs/superpowers/plans/2026-06-09-campaign-creation-eval.md
new file mode 100644
index 0000000..29a099e
--- /dev/null
+++ b/docs/superpowers/plans/2026-06-09-campaign-creation-eval.md
@@ -0,0 +1,807 @@
+# Campaign-creation Eval Implementation Plan
+
+> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
+
+**Goal:** A first `Microsoft.Extensions.AI.Evaluation` eval that drives the real `AgentExecutor` through the 2-turn campaign-creation flow against the cached real model and asserts the tool-call order with a custom evaluator.
+
+**Architecture:** New `WretchedWhispers.Evals` xUnit project. A custom `ToolCallOrderEvaluator` (deterministic, unit-testable with no model) checks `FunctionCallContent` order on a `ChatResponse` against an `ExpectedToolCallOrderContext`. An `EvalHost` builds production wiring over in-memory SQLite; an `EvalTurnRunner` runs one CharacterCreation turn (mirroring `TurnCoordinator` minus SSE/transaction) and reports the ordered tool calls. `CampaignCreationEvals` ties it to a disk-based `ReportingConfiguration` with response caching and runs two scenarios, gated on Azure creds.
+
+**Tech Stack:** C#/.NET 10, xUnit, Microsoft.Extensions.AI.Evaluation(.Reporting), Microsoft.Extensions.AI, EF Core SQLite (in-memory).
+
+**Spec:** `docs/superpowers/specs/2026-06-09-campaign-creation-eval-design.md`
+
+**API caveat:** A few exact `Microsoft.Extensions.AI.Evaluation(.Reporting)` signatures could not be fully confirmed from docs (the `EvaluationContext` base constructor, `EvaluationResult.Get<T>`, the `DiskBasedReportingConfiguration.Create` parameter list, and the `ScenarioRun.EvaluateAsync` `additionalContext` overload). Each spot below is marked **[VERIFY]**. The *shape* is fixed; if the installed package's signature differs, adapt only that binding line — do not change the design. TDD (Tasks 2–3) and compile (Task 4) will surface any mismatch immediately.
+
+**Dependency direction:** `Evals` references `Api`, `Infrastructure`, `Core` (it drives the real agent). It does NOT reference `WretchedWhispers.Tests`.
+
+---
+
+## Task 1: Scaffold the `WretchedWhispers.Evals` project
+
+**Files:**
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj`
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/.gitignore`
+- Modify: `WrtechedWhispers/WrtechedWhispers.sln`
+
+- [ ] **Step 1: Create the project file**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj`:
+
+```xml
+<Project Sdk="Microsoft.NET.Sdk">
+
+  <PropertyGroup>
+    <TargetFramework>net10.0</TargetFramework>
+    <Nullable>enable</Nullable>
+    <ImplicitUsings>enable</ImplicitUsings>
+    <IsPackable>false</IsPackable>
+    <!-- Exclude from the default `dotnet test` CI path; run explicitly by name. -->
+    <IsTestProject>true</IsTestProject>
+  </PropertyGroup>
+
+  <ItemGroup>
+    <PackageReference Include="Microsoft.Extensions.AI.Evaluation" />
+    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting" />
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="xunit.v3" />
+    <PackageReference Include="xunit.runner.visualstudio" />
+  </ItemGroup>
+
+  <ItemGroup>
+    <ProjectReference Include="..\WretchedWhispers.Api\WretchedWhispers.Api.csproj" />
+    <ProjectReference Include="..\WretchedWhispers.Infrastructure\WretchedWhispers.Infrastructure.csproj" />
+    <ProjectReference Include="..\WretchedWhispers.Core\WretchedWhispers.Core.csproj" />
+  </ItemGroup>
+
+</Project>
+```
+
+Note on versions: if the existing test project pins package versions inline (check `WretchedWhispers.Tests.csproj` for `Version=` on `xunit`/`Microsoft.NET.Test.Sdk`), copy those exact versions onto the matching references here. For the two evaluation packages, add explicit `Version="..."` using the latest stable resolved in Step 2.
+
+- [ ] **Step 2: Add the evaluation packages (resolves latest compatible versions)**
+
+Run:
+```bash
+cd WrtechedWhispers/WretchedWhispers.Evals
+dotnet add package Microsoft.Extensions.AI.Evaluation
+dotnet add package Microsoft.Extensions.AI.Evaluation.Reporting
+dotnet add package Microsoft.NET.Test.Sdk
+dotnet add package xunit.v3
+dotnet add package xunit.runner.visualstudio
+cd -
+```
+Expected: packages restore; the csproj now has concrete versions. (xunit.v3 is used deliberately — it provides `Assert.Skip`/`Assert.SkipWhen` for the creds-gated evals; the existing `WretchedWhispers.Tests` project's xunit v2 can't skip conditionally. Mixing v2 and v3 across separate projects is fine.)
+
+- [ ] **Step 3: Add the gitignore for the local results/cache dir**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/.gitignore`:
+
+```gitignore
+# Local-only eval response cache + stored results (not a committed CI artifact)
+.eval-results/
+```
+
+- [ ] **Step 4: Add the project to the solution**
+
+Run:
+```bash
+dotnet sln WrtechedWhispers/WrtechedWhispers.sln add WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
+```
+Expected: "Project ... added to the solution."
+
+- [ ] **Step 5: Build to confirm the project + packages compile**
+
+Run: `dotnet build WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj`
+Expected: `0 errors` (NU1902 OpenTelemetry warnings from referenced projects are pre-existing noise).
+
+- [ ] **Step 6: Commit**
+
+```bash
+git add WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj WrtechedWhispers/WretchedWhispers.Evals/.gitignore WrtechedWhispers/WrtechedWhispers.sln
+git commit -m "chore: scaffold WretchedWhispers.Evals project with AI.Evaluation packages"
+```
+
+---
+
+## Task 2: `ToolCallOrderEvaluator` + `ExpectedToolCallOrderContext` (TDD, no model)
+
+The evaluator is deterministic — fully unit-testable by handing it a `ChatResponse` carrying `FunctionCallContent`s and an expected-order context. No Azure creds needed.
+
+**Files:**
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ExpectedToolCallOrderContext.cs`
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluator.cs`
+- Test: `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluatorTests.cs`
+
+- [ ] **Step 1: Write the failing tests**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluatorTests.cs`:
+
+```csharp
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.AI.Evaluation;
+using Xunit;
+
+namespace WretchedWhispers.Evals.Evaluators;
+
+public class ToolCallOrderEvaluatorTests
+{
+    private static ChatResponse ResponseWithToolCalls(params string[] names)
+    {
+        var contents = names
+            .Select((n, i) => (AIContent)new FunctionCallContent($"call_{i}", n))
+            .ToList();
+        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
+    }
+
+    private static async Task<BooleanMetric> EvaluateAsync(ChatResponse response, IReadOnlyList<string> expected)
+    {
+        var evaluator = new ToolCallOrderEvaluator();
+        var context = new ExpectedToolCallOrderContext(expected);
+        EvaluationResult result = await evaluator.EvaluateAsync(
+            messages: [],
+            modelResponse: response,
+            additionalContext: [context]);
+
+        // [VERIFY] EvaluationResult.Get<T>(string) — adapt to the installed API if it differs
+        // (e.g. result.Metrics[name] cast to BooleanMetric).
+        return result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
+    }
+
+    [Fact]
+    public async Task ExactOrderMatch_Passes()
+    {
+        var response = ResponseWithToolCalls("CreateCharacter", "ConfigureCampaign", "StartCampaign");
+        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
+        Assert.True(metric.Value);
+    }
+
+    [Fact]
+    public async Task WrongOrder_Fails()
+    {
+        var response = ResponseWithToolCalls("ConfigureCampaign", "CreateCharacter", "StartCampaign");
+        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
+        Assert.False(metric.Value);
+    }
+
+    [Fact]
+    public async Task MissingTool_Fails()
+    {
+        var response = ResponseWithToolCalls("CreateCharacter", "ConfigureCampaign");
+        var metric = await EvaluateAsync(response, ["CreateCharacter", "ConfigureCampaign", "StartCampaign"]);
+        Assert.False(metric.Value);
+    }
+
+    [Fact]
+    public async Task NoToolsExpected_NoneCalled_Passes()
+    {
+        var response = ResponseWithToolCalls(); // none
+        var metric = await EvaluateAsync(response, []);
+        Assert.True(metric.Value);
+    }
+
+    [Fact]
+    public async Task NoToolsExpected_ButOneCalled_Fails()
+    {
+        var response = ResponseWithToolCalls("CreateCharacter");
+        var metric = await EvaluateAsync(response, []);
+        Assert.False(metric.Value);
+    }
+}
+```
+
+- [ ] **Step 2: Run the tests to verify they fail**
+
+Run: `dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj --filter "FullyQualifiedName~ToolCallOrderEvaluatorTests"`
+Expected: FAIL — build error, the evaluator/context types don't exist.
+
+- [ ] **Step 3: Write `ExpectedToolCallOrderContext`**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ExpectedToolCallOrderContext.cs`:
+
+```csharp
+using Microsoft.Extensions.AI.Evaluation;
+
+namespace WretchedWhispers.Evals.Evaluators;
+
+/// <summary>
+/// Carries the expected ordered tool-call names into <see cref="ToolCallOrderEvaluator"/> for a single
+/// scenario run. The strongly-typed <see cref="Expected"/> list is what the evaluator reads; the base
+/// name/content are for human-readable reporting.
+/// </summary>
+public sealed class ExpectedToolCallOrderContext : EvaluationContext
+{
+    public IReadOnlyList<string> Expected { get; }
+
+    // [VERIFY] EvaluationContext base constructor shape (name + string content). If the installed
+    // package exposes a different base ctor, adapt this base(...) call only — Expected is unchanged.
+    public ExpectedToolCallOrderContext(IReadOnlyList<string> expected)
+        : base(
+            name: "Expected Tool Call Order",
+            content: expected.Count == 0 ? "(no tools)" : string.Join(" -> ", expected))
+    {
+        Expected = expected;
+    }
+}
+```
+
+- [ ] **Step 4: Write `ToolCallOrderEvaluator`**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/Evaluators/ToolCallOrderEvaluator.cs`:
+
+```csharp
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.AI.Evaluation;
+
+namespace WretchedWhispers.Evals.Evaluators;
+
+/// <summary>
+/// Deterministic, non-AI evaluator: checks that the tool calls in a <see cref="ChatResponse"/> match an
+/// expected ordered sequence EXACTLY (same tools, same order, no extras). Expected order is supplied via
+/// an <see cref="ExpectedToolCallOrderContext"/> in <c>additionalContext</c>; actual order is read from
+/// the response's <see cref="FunctionCallContent"/>s.
+/// </summary>
+public sealed class ToolCallOrderEvaluator : IEvaluator
+{
+    public const string MetricName = "Tool Call Order";
+
+    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];
+
+    public ValueTask<EvaluationResult> EvaluateAsync(
+        IEnumerable<ChatMessage> messages,
+        ChatResponse modelResponse,
+        ChatConfiguration? chatConfiguration = null,
+        IEnumerable<EvaluationContext>? additionalContext = null,
+        CancellationToken cancellationToken = default)
+    {
+        var metric = new BooleanMetric(MetricName);
+        var result = new EvaluationResult(metric);
+
+        if (additionalContext?.OfType<ExpectedToolCallOrderContext>().FirstOrDefault() is not { } context)
+        {
+            metric.AddDiagnostics(EvaluationDiagnostic.Error(
+                $"No {nameof(ExpectedToolCallOrderContext)} was supplied in {nameof(additionalContext)}."));
+            return new ValueTask<EvaluationResult>(result);
+        }
+
+        List<string> actual = modelResponse.Messages
+            .SelectMany(m => m.Contents)
+            .OfType<FunctionCallContent>()
+            .Select(c => c.Name)
+            .ToList();
+
+        bool passed = actual.SequenceEqual(context.Expected, StringComparer.Ordinal);
+
+        metric.Value = passed;
+        metric.AddDiagnostics(EvaluationDiagnostic.Informational(
+            $"expected: [{string.Join(", ", context.Expected)}]; actual: [{string.Join(", ", actual)}]"));
+
+        return new ValueTask<EvaluationResult>(result);
+    }
+}
+```
+
+- [ ] **Step 5: Run the tests to verify they pass**
+
+Run: `dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj --filter "FullyQualifiedName~ToolCallOrderEvaluatorTests"`
+Expected: PASS — 5 passed. (If a **[VERIFY]** binding line fails to compile, adapt only that line to the installed API and re-run.)
+
+- [ ] **Step 6: Commit**
+
+```bash
+git add WrtechedWhispers/WretchedWhispers.Evals/Evaluators/
+git commit -m "feat(evals): ToolCallOrderEvaluator + ExpectedToolCallOrderContext"
+```
+
+---
+
+## Task 3: `EvalHost` + `EvalTurnRunner` harness (TDD via scripted client, no model)
+
+The harness builds production wiring over in-memory SQLite and runs one CharacterCreation turn through the real `AgentExecutor`, capturing the ordered tool-call names. Tested deterministically with a scripted `IChatClient` (canned tool call) — no Azure creds.
+
+**Files:**
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/Harness/ScriptedChatClient.cs`
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalHost.cs`
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs`
+- Test: `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunnerTests.cs`
+
+- [ ] **Step 1: Write the scripted chat client (test double, replicated locally)**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/Harness/ScriptedChatClient.cs`:
+
+```csharp
+using System.Runtime.CompilerServices;
+using Microsoft.Extensions.AI;
+
+namespace WretchedWhispers.Evals.Harness;
+
+/// <summary>Replays a fixed queue of ChatResponses — used only to test the harness plumbing without a real model.</summary>
+public sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
+{
+    private readonly Queue<ChatResponse> _responses = new(responses);
+
+    public Task<ChatResponse> GetResponseAsync(
+        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
+    {
+        var response = _responses.Count > 0
+            ? _responses.Dequeue()
+            : new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty));
+        return Task.FromResult(response);
+    }
+
+    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
+        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
+        [EnumeratorCancellation] CancellationToken cancellationToken = default)
+    {
+        var response = await GetResponseAsync(messages, options, cancellationToken);
+        foreach (var message in response.Messages)
+            yield return new ChatResponseUpdate(message.Role, message.Contents);
+    }
+
+    public object? GetService(Type serviceType, object? serviceKey = null) => null;
+    public void Dispose() { }
+}
+```
+
+- [ ] **Step 2: Write the failing harness test**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunnerTests.cs`:
+
+```csharp
+using Microsoft.Extensions.AI;
+using Xunit;
+
+namespace WretchedWhispers.Evals.Harness;
+
+public class EvalTurnRunnerTests
+{
+    [Fact]
+    public async Task RunTurn_CapturesToolCallOrder_FromExecutorEvents()
+    {
+        // The scripted model: first call asks for CreateCharacter, second call narrates the result.
+        var client = new ScriptedChatClient(
+            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
+            {
+                new FunctionCallContent("call_1", "CreateCharacter", new Dictionary<string, object?> { ["name"] = "Grim" })
+            })),
+            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Grim claws free of the muck.")));
+
+        await using var host = await EvalHost.CreateAsync(client);
+        var runner = host.CreateTurnRunner();
+
+        var outcome = await runner.RunTurnAsync("Grim");
+
+        Assert.Equal(new[] { "CreateCharacter" }, outcome.ToolCalls);
+        // The packaged response carries the same calls as FunctionCallContent for the evaluator.
+        var packagedNames = outcome.Response.Messages
+            .SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.Name);
+        Assert.Equal(new[] { "CreateCharacter" }, packagedNames);
+    }
+}
+```
+
+- [ ] **Step 3: Run the test to verify it fails**
+
+Run: `dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj --filter "FullyQualifiedName~EvalTurnRunnerTests"`
+Expected: FAIL — build error, `EvalHost`/`EvalTurnRunner` don't exist.
+
+- [ ] **Step 4: Write `EvalHost`**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalHost.cs`. This mirrors the in-memory setup in `WretchedWhispers.Tests` (`SqliteTestBase` + `AgentExecutorIntegrationTests`) and the session bootstrap in `SessionEndpoints.CreateSession`. It registers the real domain services via `AddDomainServices`, opens an in-memory SQLite connection, seeds an empty campaign + chat session (so the first turn derives the `CharacterCreation` stage), and exposes a factory for `EvalTurnRunner`.
+
+```csharp
+using Microsoft.Data.Sqlite;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging.Abstractions;
+using WretchedWhispers.Api.Services;
+using WretchedWhispers.Core;
+using WretchedWhispers.Core.Campaigns;
+using WretchedWhispers.Core.Dices;
+using WretchedWhispers.Infrastructure;
+using WretchedWhispers.Infrastructure.Persistence;
+
+namespace WretchedWhispers.Evals.Harness;
+
+/// <summary>
+/// Builds production wiring (real Core services + tools) over an in-memory SQLite database for an eval
+/// run, seeded with an empty campaign + chat session so turns start in the CharacterCreation stage. The
+/// supplied <paramref name="chatClient"/> is the (cached) client the agent will call.
+/// </summary>
+public sealed class EvalHost : IAsyncDisposable
+{
+    private const string TestUserId = "eval-user";
+
+    private readonly SqliteConnection _connection;
+    private readonly ServiceProvider _provider;
+    private readonly IChatClient _chatClient;
+
+    public Guid SessionId { get; }
+    public Guid ChatSessionId { get; }
+
+    private EvalHost(SqliteConnection connection, ServiceProvider provider, IChatClient chatClient, Guid sessionId, Guid chatSessionId)
+    {
+        _connection = connection;
+        _provider = provider;
+        _chatClient = chatClient;
+        SessionId = sessionId;
+        ChatSessionId = chatSessionId;
+    }
+
+    public static async Task<EvalHost> CreateAsync(IChatClient chatClient)
+    {
+        var connection = new SqliteConnection("DataSource=:memory:");
+        connection.Open();
+
+        var services = new ServiceCollection();
+        services.AddDbContext<WretchedWhispersDbContext>(o => o.UseSqlite(connection));
+        // AddDomainServices already registers ITenantContext (scoped), repositories, Core services,
+        // Dice, IChatHistoryRepository, etc. We only add the DbContext (host's responsibility) and set
+        // the tenant user id per scope below.
+        services.AddDomainServices();
+
+        var provider = services.BuildServiceProvider();
+
+        // Create the schema.
+        await using (var scope = provider.CreateAsyncScope())
+        {
+            var db = scope.ServiceProvider.GetRequiredService<WretchedWhispersDbContext>();
+            await db.Database.EnsureCreatedAsync();
+        }
+
+        // Seed an empty campaign + chat session (mirrors SessionEndpoints.CreateSession).
+        Guid sessionId;
+        Guid chatSessionId;
+        await using (var scope = provider.CreateAsyncScope())
+        {
+            var sp = scope.ServiceProvider;
+            ((TenantContext)sp.GetRequiredService<ITenantContext>()).SetUserId(TestUserId);
+
+            var campaignsRepo = sp.GetRequiredService<ICampaignsRepository>();
+            var chatRepo = sp.GetRequiredService<IChatHistoryRepository>();
+
+            var campaign = Campaign.Create(DiceExpr.Parse("d6"), "Eval Campaign", "A doomed eval run");
+            await campaignsRepo.SaveCampaign(campaign, TestUserId);
+            await chatRepo.CreateSession(campaign.Id);
+
+            sessionId = campaign.Id;
+            chatSessionId = (await chatRepo.GetSessionsForCampaign(sessionId, CancellationToken.None)).First();
+        }
+
+        return new EvalHost(connection, provider, chatClient, sessionId, chatSessionId);
+    }
+
+    /// <summary>Creates a turn runner bound to a fresh DI scope (one scope per turn, like a request).</summary>
+    public EvalTurnRunner CreateTurnRunner()
+    {
+        var scope = _provider.CreateAsyncScope();
+        var sp = scope.ServiceProvider;
+        ((TenantContext)sp.GetRequiredService<ITenantContext>()).SetUserId(TestUserId);
+
+        var contextLoader = new SessionContextLoader(
+            sp.GetRequiredService<ICampaignsRepository>(),
+            sp.GetRequiredService<ICharactersRepository>(),
+            sp.GetRequiredService<IEncountersRepository>(),
+            NullLogger<SessionContextLoader>.Instance);
+
+        var toolProvider = new AgentToolProvider(sp, NullLogger<AgentToolProvider>.Instance);
+
+        var chatRepo = sp.GetRequiredService<IChatHistoryRepository>();
+        var executor = new AgentExecutor(
+            _chatClient,
+            chatRepo,
+            new ChatHistoryReducer(_chatClient, NullLogger<ChatHistoryReducer>.Instance),
+            new PromptComposer(),
+            NullLogger<AgentExecutor>.Instance);
+
+        return new EvalTurnRunner(scope, contextLoader, toolProvider, executor, chatRepo, SessionId, ChatSessionId);
+    }
+
+    public async ValueTask DisposeAsync()
+    {
+        await _provider.DisposeAsync();
+        _connection.Close();
+        _connection.Dispose();
+    }
+}
+```
+
+Note: confirm the exact constructor parameter lists of `SessionContextLoader`, `AgentToolProvider`, `AgentExecutor`, and `ChatHistoryReducer` against the current source (they are stable, but read them once). Confirm `IChatHistoryRepository.CreateSession` / `GetSessionsForCampaign` signatures the same way.
+
+- [ ] **Step 5: Write `EvalTurnRunner`**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/Harness/EvalTurnRunner.cs`. This deliberately mirrors `TurnCoordinator`'s per-turn sequence (load context → derive stage → get tools → save user msg → run executor → save assistant msg) without the transaction/SSE, so it must not silently drift from `TurnCoordinator`.
+
+```csharp
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.DependencyInjection;
+using WretchedWhispers.Api.Models;
+using WretchedWhispers.Api.Services;
+using WretchedWhispers.Infrastructure.Persistence;
+
+namespace WretchedWhispers.Evals.Harness;
+
+/// <summary>The captured result of one eval turn: the ordered tool-call names, plus a ChatResponse that
+/// packages those calls as FunctionCallContent for the evaluator.</summary>
+public sealed record TurnOutcome(IReadOnlyList<string> ToolCalls, ChatResponse Response, string Narrative);
+
+/// <summary>
+/// Runs one CharacterCreation turn through the real <see cref="AgentExecutor"/> and captures the tool
+/// calls. Mirrors <see cref="TurnCoordinator"/>'s per-turn steps minus the transaction/SSE layer.
+/// </summary>
+public sealed class EvalTurnRunner(
+    AsyncServiceScope scope,
+    ISessionContextLoader contextLoader,
+    IAgentToolProvider toolProvider,
+    IAgentExecutor agentExecutor,
+    IChatHistoryRepository chatHistoryRepository,
+    Guid sessionId,
+    Guid chatSessionId) : IAsyncDisposable
+{
+    public async Task<TurnOutcome> RunTurnAsync(string playerMessage, CancellationToken ct = default)
+    {
+        var context = await contextLoader.LoadAsync(sessionId, ct);
+        var stage = context.DeriveStage();
+        var (tools, _) = toolProvider.GetToolsForStage(context, stage);
+
+        await chatHistoryRepository.SaveMessage(chatSessionId, new ChatMessage(ChatRole.User, playerMessage), ct);
+
+        var toolCalls = new List<string>();
+        var narrative = new System.Text.StringBuilder();
+
+        await foreach (var evt in agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct))
+        {
+            if (evt is ToolResult tr)
+                toolCalls.Add(tr.Function);
+            else if (evt is NarrativeChunk chunk)
+                narrative.Append(chunk.Text);
+        }
+
+        await chatHistoryRepository.SaveMessage(
+            chatSessionId,
+            new ChatMessage(ChatRole.Assistant, narrative.ToString()) { AuthorName = "Game_Master" },
+            ct);
+
+        var response = BuildToolCallResponse(toolCalls, narrative.ToString());
+        return new TurnOutcome(toolCalls, response, narrative.ToString());
+    }
+
+    // Packages the captured tool-call order as FunctionCallContent so ToolCallOrderEvaluator can read it
+    // from a ChatResponse the same way it would read any real model response.
+    private static ChatResponse BuildToolCallResponse(IReadOnlyList<string> toolCalls, string narrative)
+    {
+        var contents = new List<AIContent>();
+        for (int i = 0; i < toolCalls.Count; i++)
+            contents.Add(new FunctionCallContent($"call_{i}", toolCalls[i]));
+        if (!string.IsNullOrEmpty(narrative))
+            contents.Add(new TextContent(narrative));
+        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
+    }
+
+    public ValueTask DisposeAsync() => scope.DisposeAsync();
+}
+```
+
+- [ ] **Step 6: Run the harness test to verify it passes**
+
+Run: `dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj --filter "FullyQualifiedName~EvalTurnRunnerTests"`
+Expected: PASS — 1 passed. This proves the harness builds the real CharacterCreation tools, the scripted tool call is auto-invoked against the in-memory DB, and the runner captures the order. If a constructor signature differs from Step 4's note, fix the binding and re-run.
+
+- [ ] **Step 7: Commit**
+
+```bash
+git add WrtechedWhispers/WretchedWhispers.Evals/Harness/
+git commit -m "feat(evals): EvalHost + EvalTurnRunner harness over in-memory SQLite"
+```
+
+---
+
+## Task 4: `CampaignCreationEvals` scenarios (real model + caching, creds-gated)
+
+Wires the harness + evaluator to a disk-based `ReportingConfiguration` with response caching, and runs the two scenarios against the cached real model. Skips cleanly when Azure creds are absent.
+
+**Files:**
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs`
+- Create: `WrtechedWhispers/WretchedWhispers.Evals/README.md`
+
+- [ ] **Step 1: Confirm the Azure env-var names the app already uses**
+
+Read `WrtechedWhispers/WretchedWhispers.Api/Configuration/AgentConfiguration.cs` and note the exact configuration keys/env vars it reads for the Azure OpenAI endpoint, API key, and deployment name. Use those SAME names below so a dev configures creds once. (Record them in the README in Step 4.)
+
+- [ ] **Step 2: Write the scenarios**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs`. Replace the three `AZURE_*` env-var names in `TryCreateAzureChatClient` with the exact names found in Step 1 if they differ.
+
+```csharp
+using System.ClientModel;
+using Azure.AI.OpenAI;
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.AI.Evaluation;
+using Microsoft.Extensions.AI.Evaluation.Reporting;
+using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
+using WretchedWhispers.Evals.Evaluators;
+using WretchedWhispers.Evals.Harness;
+using Xunit;
+
+namespace WretchedWhispers.Evals;
+
+public class CampaignCreationEvals
+{
+    private static readonly string[] CreateCampaignTools =
+        ["CreateCharacter", "ConfigureCampaign", "StartCampaign"];
+
+    [Fact]
+    public async Task Turn1_Begin_CallsNoTools()
+    {
+        var chatClient = TryCreateAzureChatClient();
+        Assert.SkipWhen(chatClient is null, "Azure OpenAI credentials not configured; skipping live eval.");
+
+        await using var host = await EvalHost.CreateAsync(chatClient!);
+        await using var reporting = CreateReportingConfiguration(chatClient!);
+
+        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation/Turn1-Begin");
+
+        var runner = host.CreateTurnRunner();
+        var outcome = await runner.RunTurnAsync("begin");
+
+        // [VERIFY] ScenarioRun.EvaluateAsync overload that accepts additionalContext.
+        EvaluationResult result = await run.EvaluateAsync(
+            messages: [],
+            modelResponse: outcome.Response,
+            additionalContext: [new ExpectedToolCallOrderContext([])]);
+
+        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
+        Assert.True(metric.Value, $"Expected no tools on 'begin'; got [{string.Join(", ", outcome.ToolCalls)}]");
+    }
+
+    [Fact]
+    public async Task Turn2_Name_CreatesCampaignInOrder()
+    {
+        var chatClient = TryCreateAzureChatClient();
+        Assert.SkipWhen(chatClient is null, "Azure OpenAI credentials not configured; skipping live eval.");
+
+        await using var host = await EvalHost.CreateAsync(chatClient!);
+        await using var reporting = CreateReportingConfiguration(chatClient!);
+
+        // Turn 1 first so the model has asked for a name and history is consistent.
+        var beginRunner = host.CreateTurnRunner();
+        await beginRunner.RunTurnAsync("begin");
+
+        await using ScenarioRun run = await reporting.CreateScenarioRunAsync("CampaignCreation/Turn2-Name");
+
+        var runner = host.CreateTurnRunner();
+        var outcome = await runner.RunTurnAsync("Grim");
+
+        EvaluationResult result = await run.EvaluateAsync(
+            messages: [],
+            modelResponse: outcome.Response,
+            additionalContext: [new ExpectedToolCallOrderContext(CreateCampaignTools)]);
+
+        var metric = result.Get<BooleanMetric>(ToolCallOrderEvaluator.MetricName);
+        Assert.True(metric.Value,
+            $"Expected [{string.Join(", ", CreateCampaignTools)}]; got [{string.Join(", ", outcome.ToolCalls)}]");
+    }
+
+    // [VERIFY] DiskBasedReportingConfiguration.Create parameter list (storageRootPath, evaluators,
+    // chatConfiguration, enableResponseCaching). Adapt to the installed factory if it differs; the
+    // intent is: disk storage at .eval-results, our evaluator, the given chat client, caching ON.
+    private static ReportingConfiguration CreateReportingConfiguration(IChatClient chatClient)
+    {
+        var chatConfiguration = new ChatConfiguration(chatClient);
+        return DiskBasedReportingConfiguration.Create(
+            storageRootPath: Path.Combine(AppContext.BaseDirectory, ".eval-results"),
+            evaluators: [new ToolCallOrderEvaluator()],
+            chatConfiguration: chatConfiguration,
+            enableResponseCaching: true,
+            executionName: "campaign-creation");
+    }
+
+    // Mirrors the app's AgentConfiguration Azure wiring. Returns null if creds are absent so the
+    // [Fact] can skip. Replace the env-var names with the exact ones from AgentConfiguration (Step 1).
+    private static IChatClient? TryCreateAzureChatClient()
+    {
+        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
+        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
+        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
+        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(deployment))
+            return null;
+
+        var azure = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
+        return azure.GetChatClient(deployment).AsIChatClient();
+    }
+}
+```
+
+- [ ] **Step 3: Build to confirm it compiles**
+
+Run: `dotnet build WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj`
+Expected: `0 errors`. Resolve any **[VERIFY]** binding mismatches against the installed package surface (read the package's public types if a member is missing). `Assert.SkipWhen` is provided by xunit.v3 (pinned in Task 1).
+
+- [ ] **Step 4: Run the eval tests (skip path, no creds)**
+
+Run: `dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj --filter "FullyQualifiedName~CampaignCreationEvals"`
+Expected: both tests SKIP with the "credentials not configured" message (CI/dev without creds). This proves the gating works.
+
+- [ ] **Step 5: Write the README**
+
+Create `WrtechedWhispers/WretchedWhispers.Evals/README.md`:
+
+```markdown
+# WretchedWhispers.Evals
+
+Behavioral evals for the AI game master, built on `Microsoft.Extensions.AI.Evaluation`. Unlike the unit
+tests (scripted, deterministic), these drive the **real** model and **score** behavior. Excluded from the
+default CI test run.
+
+## Running
+
+Set the Azure OpenAI credentials the app uses (see `AgentConfiguration`):
+
+```bash
+export AZURE_OPENAI_ENDPOINT="https://<resource>.openai.azure.com/"
+export AZURE_OPENAI_API_KEY="<key>"
+export AZURE_OPENAI_DEPLOYMENT="<deployment>"
+
+dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj
+```
+
+Without these set, the evals **skip**. The first run hits Azure and writes a response cache under
+`.eval-results/` (git-ignored); later runs replay from the cache and are free + deterministic.
+
+## Reporting
+
+Results are stored under `.eval-results/`. Render an HTML report with the eval console tool:
+
+```bash
+dotnet tool install --global Microsoft.Extensions.AI.Evaluation.Console
+dotnet aieval report --path WrtechedWhispers/WretchedWhispers.Evals/bin/Debug/net10.0/.eval-results --output eval-report.html
+```
+
+## Current evals
+
+- **CampaignCreation/Turn1-Begin** — "begin" must call no tools (asks for a name).
+- **CampaignCreation/Turn2-Name** — a name must trigger `CreateCharacter -> ConfigureCampaign -> StartCampaign`, in that exact order.
+```
+
+(Replace the env-var names if Step 1 found different ones. Confirm the `.eval-results` path matches the `storageRootPath` used in `CreateReportingConfiguration` — both resolve under the test `bin` output via `AppContext.BaseDirectory`.)
+
+- [ ] **Step 6: Commit**
+
+```bash
+git add WrtechedWhispers/WretchedWhispers.Evals/CampaignCreationEvals.cs WrtechedWhispers/WretchedWhispers.Evals/README.md
+git commit -m "feat(evals): campaign-creation tool-call-order scenarios (real model + cache)"
+```
+
+---
+
+## Task 5: Final verification & PR
+
+- [ ] **Step 1: Build the whole solution**
+
+Run: `dotnet build WrtechedWhispers/WrtechedWhispers.sln`
+Expected: `0 errors`.
+
+- [ ] **Step 2: Confirm the default test suite is unaffected**
+
+Run: `dotnet test WrtechedWhispers/WretchedWhispers.Tests/WretchedWhispers.Tests.csproj`
+Expected: still 354 passed, 2 skipped (the Evals project is a separate csproj and is not part of this run).
+
+- [ ] **Step 3: Confirm the Evals project's own deterministic tests pass and the live evals skip**
+
+Run: `dotnet test WrtechedWhispers/WretchedWhispers.Evals/WretchedWhispers.Evals.csproj`
+Expected: the evaluator tests (5) and harness test (1) PASS; the two `CampaignCreationEvals` SKIP (no creds). Net: 6 passed, 2 skipped.
+
+- [ ] **Step 4: Push and open the PR**
+
+```bash
+git push -u origin feat/campaign-creation-eval
+```
+Then open a PR against `main` (the push prints a compare URL; `gh` is not installed in this environment). Title: `First eval: campaign-creation tool-call order (Microsoft.Extensions.AI.Evaluation)`. Hand to the user to merge.
+
+---
+
+## Notes / Known follow-ups (out of scope)
+
+- Quality evaluators (narration coherence/groundedness) on the same scenarios — a later eval.
+- Combat / exploration / resolution evals — later.
+- Wiring evals into a separate CI job that injects creds and replays a committed cache — deferred (cache is local for now).
diff --git a/docs/superpowers/specs/2026-06-09-campaign-creation-eval-design.md b/docs/superpowers/specs/2026-06-09-campaign-creation-eval-design.md
new file mode 100644
index 0000000..e7ac0d9
--- /dev/null
+++ b/docs/superpowers/specs/2026-06-09-campaign-creation-eval-design.md
@@ -0,0 +1,165 @@
+# Campaign-creation tool-call-order eval (Microsoft.Extensions.AI.Evaluation)
+
+**Date:** 2026-06-09
+**Status:** Approved (brainstorming), pending implementation plan
+
+## Problem
+
+The app's behavioral bugs (model failing to advance stages, fabricating combat) are exactly the
+kind of regression a scripted unit test cannot catch but a real-model eval can. We want to start an
+eval suite with **one simple, deterministic eval**: does the real model, driven through the app's
+CharacterCreation-stage agent, create a campaign by calling the right tools in the right order?
+
+Per `StagePrompts.CharacterCreation` the flow is two turns:
+
+- Turn 1 (player: `"begin"`) → the model should ask for a character name and call **no tools**
+  (the known "begin shouldn't create a character" failure mode).
+- Turn 2 (player: a name) → the model should call **`CreateCharacter` → `ConfigureCampaign` →
+  `StartCampaign`**, in that exact order.
+
+## Decisions (locked during brainstorming, 2026-06-09)
+
+1. **Eval target:** the **real** Azure OpenAI model, with **response caching** so reruns are free and
+   reproducible. First run (with creds) records to a local cache; later runs replay. Skips when no
+   creds. This is what makes it an *eval* (measures real behavior) rather than a second unit test.
+2. **Seam under eval:** drive the real `AgentExecutor.ExecuteAsync` per turn (not the full HTTP/SSE
+   loop, not a parallel `IChatClient` harness). Tool-call order is read from the `ToolResult`
+   `GameTurnEvent`s the executor already emits.
+3. **Scenario scope:** the full 2-turn flow (turn 1 asserts zero tools; turn 2 asserts the ordered
+   triple).
+4. **Location:** a new `WretchedWhispers.Evals` xUnit project, kept out of the normal `dotnet test`
+   CI path.
+
+## API caveat
+
+Exact `Microsoft.Extensions.AI.Evaluation` API names below (`DiskBasedReportingConfiguration.Create`,
+`CreateScenarioRunAsync`, `ScenarioRun.ChatConfiguration`, `EvaluationContext`, `BooleanMetric`, the
+`IEvaluator.EvaluateAsync` parameter list, the `enableResponseCaching` switch) are taken from the
+library's documented shape but must be **verified against the installed package version during
+implementation** (Context7 `/dotnet/extensions` + the package's public surface). The architecture
+below does not change if a name or parameter differs; only the binding does.
+
+## Design
+
+### Project & packages
+
+New xUnit project `WretchedWhispers.Evals`:
+
+- **Packages:** `Microsoft.Extensions.AI.Evaluation` (core abstractions + metrics),
+  `Microsoft.Extensions.AI.Evaluation.Reporting` (response caching + result storage). NOT
+  `.Quality`/`.Safety`/`.NLP` — those are LLM-judge / text-metric evaluators irrelevant to a
+  structural tool-order check (YAGNI).
+- **References:** `WretchedWhispers.Api`, `WretchedWhispers.Infrastructure`, `WretchedWhispers.Core`.
+  Does NOT reference `WretchedWhispers.Tests`; the small in-memory-DB harness is replicated locally.
+- **Layout:**
+  - `Harness/EvalHost.cs` — builds in-memory SQLite `WretchedWhispersDbContext` + real Core services +
+    `AgentToolProvider` + `SessionContextLoader` + `PromptComposer` + in-memory chat-history store.
+    Mirrors the existing integration-test setup, but parameterized by the `IChatClient` to use.
+  - `Harness/EvalTurnRunner.cs` — runs one CharacterCreation turn and returns the ordered tool-call
+    names.
+  - `Evaluators/ExpectedToolCallOrderContext.cs` — `EvaluationContext` carrying the expected order.
+  - `Evaluators/ToolCallOrderEvaluator.cs` — the custom `IEvaluator`.
+  - `CampaignCreationEvals.cs` — the `[Fact]` scenarios + reporting-configuration wiring.
+  - `README.md` — how to run (env vars) and how to render the report (`dotnet aieval`).
+- **Results/cache dir** `WretchedWhispers.Evals/.eval-results/` is **git-ignored** (local-only cache,
+  not a committed CI artifact).
+
+### Harness
+
+`EvalHost` assembles production wiring once: an in-memory SQLite context (shared-connection pattern
+from `SqliteTestBase`), real Core services so the campaign tools genuinely succeed, the real
+`AgentToolProvider`, `SessionContextLoader`, `PromptComposer`, and an in-memory chat-history store.
+Same shape as `AgentExecutorIntegrationTests`, but with a real `IChatClient` (supplied by the caller)
+rather than the scripted one. A real DB (not mocks) is used so `CreateCharacter`/`ConfigureCampaign`/
+`StartCampaign` actually persist and the model proceeds through the chain like production, and so
+turn 1's state/history flows into turn 2.
+
+`EvalTurnRunner.RunTurnAsync(playerMessage)` reproduces what `TurnCoordinator` does per turn, minus
+the SSE/transaction machinery (irrelevant to tool order):
+
+1. `SessionContextLoader.LoadAsync` → `SessionContext`; `DeriveStage()` (CharacterCreation).
+2. `AgentToolProvider.GetToolsForStage(context, stage)`.
+3. `agentExecutor.ExecuteAsync(tools, context, chatSessionId, playerMessage, ct)`, collecting every
+   `ToolResult` event in order → `IReadOnlyList<string>` of tool names. Persist the user + assistant
+   messages to the in-memory history so the next turn sees this exchange.
+
+It returns the ordered tool-call names. The code comment notes it intentionally mirrors
+`TurnCoordinator`'s load/persist steps so the duplication does not silently drift.
+
+**Caching wiring (the crux):** `AgentExecutor` takes an `IChatClient`. The harness is given the
+**cached** client the reporting layer exposes (`scenarioRun.ChatConfiguration.ChatClient`) rather than
+the raw Azure client, so every model call the agent makes is recorded on first run and replayed
+thereafter.
+
+### Custom evaluator
+
+`ExpectedToolCallOrderContext : EvaluationContext` carries the expected ordered tool names into the
+evaluator per scenario run (the idiomatic way expectations reach an `IEvaluator`). E.g.
+`new ExpectedToolCallOrderContext(["CreateCharacter", "ConfigureCampaign", "StartCampaign"])`, or `[]`
+for the "begin" turn.
+
+`ToolCallOrderEvaluator : IEvaluator`:
+
+- `EvaluationMetricNames => ["Tool Call Order"]`.
+- `EvaluateAsync(messages, modelResponse, chatConfiguration, additionalContext, ct)`:
+  - **Actual** = `FunctionCallContent` names from `modelResponse`, in order. The harness packages the
+    captured tool-call order onto `modelResponse` as an assistant message whose `Contents` are
+    `FunctionCallContent(name)` items, so the evaluator stays a generic "tool-call order over a
+    `ChatResponse`" check, decoupled from `GameTurnEvent`.
+  - **Expected** = from the `ExpectedToolCallOrderContext` in `additionalContext`.
+  - Comparison = **exact ordered equality** (`actual.SequenceEqual(expected)`). Returns
+    `BooleanMetric("Tool Call Order", passed)` with an `Interpretation` marking pass/fail and
+    `Diagnostics` listing `expected: […]` vs `actual: […]`.
+
+Exact match (not "subsequence with extras allowed") is deliberate: campaign creation should call
+exactly those three tools, and a stray call — or any tool on the "begin" turn — is itself a finding.
+A mode flag to relax this can be added later if a scenario needs it.
+
+### Scenarios, reporting & caching
+
+`CampaignCreationEvals`:
+
+- Build a `ChatConfiguration` from the real Azure `IChatClient` (reusing `AgentConfiguration`). Create
+  one disk-based `ReportingConfiguration` with response caching enabled, the `ToolCallOrderEvaluator`,
+  and the `.eval-results/` storage path.
+- Two `[Fact]` scenarios sharing one `EvalHost` (so turn 1 state/history flows into turn 2):
+  - `Turn1_Begin_CallsNoTools` — scenario "CampaignCreation/Turn1-Begin"; `RunTurnAsync("begin")`;
+    evaluate with `ExpectedToolCallOrderContext([])`.
+  - `Turn2_Name_CreatesCampaignInOrder` — scenario "CampaignCreation/Turn2-Name";
+    `RunTurnAsync("Grim")`; evaluate with
+    `ExpectedToolCallOrderContext(["CreateCharacter","ConfigureCampaign","StartCampaign"])`.
+- Each scenario: `await using var run = await reportingConfig.CreateScenarioRunAsync(name)`; build the
+  agent on `run.ChatConfiguration.ChatClient`; run the turn; package the captured order onto a
+  `ChatResponse`; `await run.EvaluateAsync(messages, response, additionalContext: [expected])`.
+  Disposal persists the result. The test also asserts the metric passed, so a regression fails the run.
+
+**Creds gating:** each `[Fact]` checks for the Azure env vars (endpoint/key/deployment); absent →
+`Assert.Skip`. First run with creds records the cache; later runs replay without creds. CI (no creds)
+skips cleanly.
+
+**Viewing results:** results land in `.eval-results/`; `dotnet aieval report` (the
+`Microsoft.Extensions.AI.Evaluation.Console` tool) renders an HTML report. The project README documents
+the exact command and required env vars.
+
+### Error handling
+
+- A turn that throws (model/transport error) fails the scenario loudly with the exception — no silent
+  pass.
+- A turn that returns the wrong/empty tool calls is a normal metric **failure** with diagnostics, not
+  an error.
+
+## Out of scope (YAGNI)
+
+- Quality/safety/NLP evaluators (narration coherence, groundedness) — a later eval, not this one.
+- Combat / exploration / resolution scenarios — this eval is campaign-creation only.
+- Committing the response cache as a CI artifact — the cache is local; CI skips without creds.
+- Reusing `TurnCoordinator` or the HTTP loop — deliberately driving `AgentExecutor` directly.
+
+## Verification
+
+- `dotnet build` of the new project succeeds; it is excluded from the default `dotnet test` CI path.
+- With Azure creds: both scenarios run, first pass populates `.eval-results/` cache, the metrics are
+  asserted, and `dotnet aieval report` renders a report showing both scenarios.
+- Without creds: both `[Fact]`s skip cleanly.
+- A deliberately-wrong expected order (sanity check during implementation) produces a failing
+  `BooleanMetric` with legible `expected` vs `actual` diagnostics.
```
