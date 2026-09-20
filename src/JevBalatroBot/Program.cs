using System.Text.Json;
using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Balatro.Balatrobot;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Decisions;
using JevBalatroBot.Core.Game;
using JevBalatroBot.Jev.Client;

try
{
    var options = ApplicationOptions.Load();
    using var balatrobotHttpClient = new HttpClient();
    balatrobotHttpClient.Timeout = options.BalatrobotTimeout;
    using var decisionHttpClient = new HttpClient();
    decisionHttpClient.Timeout = options.DecisionProvider == DecisionProvider.Ollama ? options.OllamaTimeout : options.JevTimeout;
    IDecisionService decisionService = options.DecisionProvider switch
    {
        DecisionProvider.Ollama => new OllamaDecisionService(decisionHttpClient, new OllamaOptions(options.OllamaEndpoint, options.OllamaModel)),
        DecisionProvider.Jev => new JevDecisionService(decisionHttpClient, new JevOptions(options.JevEndpoint, options.JevModel, options.JevApiKey)),
        _ => throw new InvalidOperationException($"Unsupported decision provider: {options.DecisionProvider}.")
    };
    var runner = new BlindRunner(
        new BalatrobotClient(balatrobotHttpClient, options.BalatrobotEndpoint, new BalatroStateReducer()),
        decisionService,
        new CandidateActionGenerator(),
        new ActionValidator(),
        new DecisionLogWriter(options.DecisionLogPath),
        new ConsoleRunObserver(options.DecisionProvider.ToString()));
    var result = await runner.RunAsync(options.NewRun, CancellationToken.None);
    Console.WriteLine($"[RESULT] {result.Result}: {result.Detail}");
    Console.WriteLine($"Run: {result.RunId}; decisions: {result.TotalDecisions}; model: {result.JevDecisions}; fallback: {result.FallbackDecisions}");
    return result.Result is JevBalatroBot.Core.Decisions.RunOutcome.Win or JevBalatroBot.Core.Decisions.RunOutcome.Loss ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"[ERROR] {exception.Message}");
    return 1;
}

internal enum DecisionProvider { Ollama, Jev }

internal sealed record ApplicationOptions(DecisionProvider DecisionProvider, Uri BalatrobotEndpoint, TimeSpan BalatrobotTimeout, Uri OllamaEndpoint, string OllamaModel, TimeSpan OllamaTimeout, Uri JevEndpoint, string JevModel, string? JevApiKey, TimeSpan JevTimeout, string DecisionLogPath, NewRunOptions NewRun)
{
    public static ApplicationOptions Load()
    {
        using var defaults = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var localPath = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");
        using var local = File.Exists(localPath) ? JsonDocument.Parse(File.ReadAllText(localPath)) : null;
        var providerValue = GetSetting("DECISION_PROVIDER", "Decision", "Provider");
        if (!Enum.TryParse<DecisionProvider>(providerValue, ignoreCase: true, out var decisionProvider))
        {
            throw new InvalidOperationException($"Decision.Provider must be 'Ollama' or 'Jev'; got '{providerValue ?? "null"}'.");
        }

        return new ApplicationOptions(
            decisionProvider,
            new Uri(GetSetting("BALATROBOT_ENDPOINT", "Balatrobot", "Endpoint")!),
            TimeSpan.FromSeconds(int.Parse(GetSetting(null, "Balatrobot", "TimeoutSeconds")!, System.Globalization.CultureInfo.InvariantCulture)),
            new Uri(GetSetting("OLLAMA_ENDPOINT", "Ollama", "Endpoint")!),
            GetSetting("OLLAMA_MODEL", "Ollama", "Model")!,
            TimeSpan.FromSeconds(int.Parse(GetSetting(null, "Ollama", "TimeoutSeconds")!, System.Globalization.CultureInfo.InvariantCulture)),
            new Uri(GetSetting("JEV_ENDPOINT", "Jev", "Endpoint")!),
            GetSetting("JEV_MODEL", "Jev", "Model")!,
            GetSetting("JEV_API_KEY", "Jev", "ApiKey"),
            TimeSpan.FromSeconds(int.Parse(GetSetting(null, "Jev", "TimeoutSeconds")!, System.Globalization.CultureInfo.InvariantCulture)),
            GetSetting("JEV_LOG_PATH", "Logging", "DecisionLogPath")!,
            new NewRunOptions(
                GetSetting("BALATRO_DECK", "Run", "Deck")!,
                GetSetting("BALATRO_STAKE", "Run", "Stake")!,
                GetSetting("BALATRO_SEED", "Run", "Seed")));

        string? GetSetting(string? environmentVariable, string section, string property)
            => (environmentVariable is null ? null : Environment.GetEnvironmentVariable(environmentVariable))
                ?? GetOptionalValue(local?.RootElement, section, property)
                ?? GetOptionalValue(defaults.RootElement, section, property);
    }

    private static string? GetOptionalValue(JsonElement? root, string section, string property)
        => root is { ValueKind: JsonValueKind.Object }
            && root.Value.TryGetProperty(section, out var child)
            && child.TryGetProperty(property, out var value)
            && value.ValueKind != JsonValueKind.Null
                ? value.ToString()
                : null;
}

internal sealed class ConsoleRunObserver(string decisionProvider) : IRunObserver
{
    public void OnProgress(string message, GameSnapshot snapshot) => Console.WriteLine($"[GAME] {message} State: {snapshot.State}");

    public void OnDecision(DecisionRecord record)
    {
        Console.WriteLine($"[{decisionProvider.ToUpperInvariant()}] Ante {record.Ante} · {record.Blind.Name} · {record.Blind.CurrentScore}/{record.Blind.RequiredScore}");
        Console.WriteLine($"Candidates: {record.CandidateActions.Count}; selected: {record.SelectedAction.Id}; source: {record.DecisionSource}; latency: {record.DecisionLatencyMs} ms");
        Console.WriteLine($"Model build: {record.GameState.JevBuildIntent ?? "not selected"}; tactical intent: {record.GameState.JevTacticalIntent ?? "not selected"}; target: {record.JevTacticalTargetHand ?? "not selected"}; expected hands: {record.JevExpectedHandsToClear?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}");
        foreach (var constraint in record.GameState.BlindConstraints) Console.WriteLine($"Boss constraint: {constraint.Summary}");
        Console.WriteLine(record.SelectedAction.Description);
        Console.WriteLine($"Hands: {record.HandsRemaining}; discards: {record.DiscardsRemaining}");
        foreach (var finding in record.AuditFindings) Console.WriteLine($"Decision audit ({finding.Severity}): {finding.Summary}");
    }
}
