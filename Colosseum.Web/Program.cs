using Anthropic.SDK;
using Colosseum.Core.Configuration;
using Colosseum.Core.Services;
using Colosseum.Web;
using Colosseum.Web.Components;
using Colosseum.Web.Hubs;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────
builder.Services.AddOptions<ColosseumOptions>()
    .BindConfiguration(ColosseumOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Claude provider ─────────────────────────────────────────────────────────
// Default: "Cli" — uses the local `claude` CLI (no API key needed).
// Set Colosseum:Provider = "Sdk" to use the Anthropic SDK with an API key.
builder.Services.AddSingleton<IClaudeProvider>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ColosseumOptions>>().Value;

    if (string.Equals(opts.Provider, "Sdk", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(opts.AnthropicApiKey))
            throw new InvalidOperationException(
                "Colosseum:Provider is \"Sdk\" but Colosseum:AnthropicApiKey is not set.");

        var client = new AnthropicClient(opts.AnthropicApiKey);
        return new AnthropicSdkProvider(client);
    }

    // Default: CLI
    return sp.GetRequiredService<ClaudeCliProvider>();
});

builder.Services.AddSingleton<ClaudeCliProvider>();

// ── Core services ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<GladiatorService>();
builder.Services.AddSingleton<SimilarityService>();
builder.Services.AddTransient<IssueTracker>();
builder.Services.AddTransient<DebateOrchestrator>();
builder.Services.AddTransient<ModeratorService>();

// ── Blazor + SignalR ────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR(opts =>
{
    opts.MaximumReceiveMessageSize = 512 * 1024; // 512 KB
});

var app = builder.Build();

// ── Pipeline ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapHub<ArenaHub>("/hubs/arena");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
