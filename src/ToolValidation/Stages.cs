using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Agents.AI;

namespace ToolValidation;

// Stage 1: the menu as an enum. The names are all the model sees.
public enum MenuItem { GfSandwichPlatter, SandwichPlatter, FruitPlatter, CheeseBoard, LemonadeJug }

public static class EnumStage
{
    [Description("Order catering for the company picnic.")]
    public static string OrderCatering(
        [Description("A dish from the menu.")] MenuItem item,
        [Description("How many to order.")] int quantity) =>
        $"Ordered {quantity} x {item}.";
}

// Stage 2: a regular expression. It goes into the schema as "pattern" and is never checked.
public static class RegexStage
{
    [Description("Cake or death?")]
    public static string CakeOrDeath(
        [Description("Your choice.")] [RegularExpression("^(cake|death)$")] string choice) =>
        $"You chose {choice}.";
}

// Stage 3: the menu lives in an API, checked by [OnMenu] at run time.
public enum Allergen { Nuts, Gluten, Dairy, Eggs }

public sealed record Suggestion(
    [property: OnMenu] string Item,
    [property: Range(1, 50)] int Quantity,
    decimal PricePerUnit) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // The price must match the caterer's own system, not whatever the model wrote down.
        var menu = (Menu?)context.GetService(typeof(Menu));
        if (menu?.Find(Item) is { } entry && entry.Price != PricePerUnit)
        {
            yield return new ValidationResult(
                $"The price of {Item} is {entry.Price}, not {PricePerUnit}.", [nameof(PricePerUnit)]);
        }
    }
}

public sealed record SuggestionList([property: MinLength(1)] Suggestion[] Suggestions);

public sealed class ApiStage(Menu menu, AIAgent? chef = null)
{
    [Description("Get the caterer's current menu.")]
    public IReadOnlyList<MenuEntry> GetMenu() => menu.Items;

    [Description("Order catering for the company picnic.")]
    public string OrderCatering(
        [Description("The id of a dish on the menu.")] [OnMenu] string item,
        [Description("How many to order.")] [Range(1, 50)] int quantity) =>
        $"Ordered {quantity} x {menu.Find(item)?.Name ?? item}.";

    [Description("Ask the chef for catering suggestions for the picnic.")]
    public async Task<SuggestionList> CateringSuggestions(
        [Description("How many people are coming.")] [Range(1, 500)] int headcount,
        [Description("Allergies to avoid.")] Allergen[] allergies)
    {
        // A subagent, called with typed input and returning a typed result that is validated
        // like any other tool's. Agent Framework's own AsAIFunction() passes a free-text query
        // in and a free-text answer out.
        var response = await (chef ?? throw new InvalidOperationException("No chef agent configured.")).RunAsync<SuggestionList>(
            $"Suggest catering for {headcount} people avoiding {string.Join(", ", allergies)}.");
        return response.Result;
    }
}
