using Anthropic.SDK;
using Colosseum.Core.Configuration;
using Colosseum.Core.Services;
using Colosseum.Web.Components;
using Colosseum.Web.Hubs;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────
builder.Services.AddOptions<ColosseumOptions>()
    .BindConfiguration(ColosseumOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Anthropic SDK ───────────────────────────────────────────────────────────
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ColosseumOptions>>().Value;
    if (string.IsNullOrWhiteSpace(opts.AnthropicApiKey))
        throw new InvalidOperationException(
            "AnthropicApiKey is not configured. Set Colosseum:AnthropicApiKey in appsettings.json or environment variables.");
    return new AnthropicClient(opts.AnthropicApiKey);
});

// ── Core services ───────────────────────────────────────────────────────────
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
