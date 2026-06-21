using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HelloWorldAgent;

public sealed class HelloWorldToolProvider
{
    public IReadOnlyList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(Greet, "hello_greet",
                "Greet a user by name with a personalized welcome message"),
            AIFunctionFactory.Create(RollDice, "hello_roll_dice",
                "Roll one or more dice and return the results"),
            AIFunctionFactory.Create(CoinFlip, "hello_coin_flip",
                "Flip a coin and return heads or tails"),
        ];
    }

    [Description("Greet a user by name with a personalized welcome message")]
    private static string Greet(
        [Description("The name of the person to greet")] string name)
    {
        return JsonSerializer.Serialize(new
        {
            greeting = $"Hello, {name}! Welcome to AI Agent Canvas.",
            timestamp = DateTime.UtcNow.ToString("o"),
        });
    }

    [Description("Roll one or more dice and return the results")]
    private static string RollDice(
        [Description("Number of dice to roll (1-10)")] int count = 1,
        [Description("Number of sides per die (default 6)")] int sides = 6)
    {
        count = Math.Clamp(count, 1, 10);
        sides = Math.Clamp(sides, 2, 100);
        var rng = Random.Shared;
        var rolls = Enumerable.Range(0, count).Select(_ => rng.Next(1, sides + 1)).ToList();
        return JsonSerializer.Serialize(new { rolls, total = rolls.Sum(), sides, count });
    }

    [Description("Flip a coin and return heads or tails")]
    private static string CoinFlip()
    {
        var result = Random.Shared.Next(2) == 0 ? "heads" : "tails";
        return JsonSerializer.Serialize(new { result });
    }
}
