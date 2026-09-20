using System.Text.Json;
using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Balatro.Balatrobot;

namespace JevBalatroBot.Tests;

public sealed class FakeJevDecisionServiceTests
{
    [Fact]
    public async Task FakeService_SelectsTheProvidedCandidateWithoutNetworkAccess()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "small-blind-state.json")));
        var state = new BalatroStateReducer().Reduce(document.RootElement);
        var candidate = new CandidateActionGenerator().Generate(state).First();

        var result = await new FakeJevDecisionService(candidate.Id).DecideAsync(state, [candidate], CancellationToken.None);

        Assert.Equal(candidate.Id, result.ActionId);
        Assert.Equal("fake-jev", result.Model);
    }
}
