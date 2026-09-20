using System.Text.Json;
using JevBalatroBot.Agent.Blind;
using JevBalatroBot.Balatro.Balatrobot;
using JevBalatroBot.Core.Actions;
using JevBalatroBot.Core.Game;

namespace JevBalatroBot.Tests;

public sealed class ActionValidatorTests
{
    [Fact]
    public void Validate_AcceptsAnExistingCurrentCandidate()
    {
        var state = ReadState();
        var candidates = new CandidateActionGenerator().Generate(state);

        var result = new ActionValidator().Validate(candidates[0].Id, candidates, state);

        Assert.True(result.IsValid);
        Assert.Equal(candidates[0], result.Action);
    }

    [Fact]
    public void Validate_RejectsAnUnknownActionId()
    {
        var state = ReadState();
        var candidates = new CandidateActionGenerator().Generate(state);

        var result = new ActionValidator().Validate("action_999", candidates, state);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsACandidateAfterTheHandChanges()
    {
        var state = ReadState();
        var candidate = new CandidateActionGenerator().Generate(state).First();
        var changedCards = state.Hand.Cards.ToArray();
        changedCards[0] = changedCards[0] with { Id = 999 };
        var changedState = state with { Hand = new HandState(state.Hand.SelectionLimit, changedCards) };

        var result = new ActionValidator().Validate(candidate.Id, [candidate], changedState);

        Assert.False(result.IsValid);
    }

    private static BlindDecisionState ReadState()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "small-blind-state.json")));
        return new BalatroStateReducer().Reduce(document.RootElement);
    }
}
