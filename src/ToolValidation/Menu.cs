using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;

namespace ToolValidation;

/// <summary>A dish as the caterer's menu API serves it.</summary>
public sealed record MenuEntry(string Id, string Name, decimal Price, string[] FreeFrom);

/// <summary>
/// The caterer's current menu, fetched from the menu API. Refresh it before each turn; the
/// validation attributes read the snapshot, because ValidationAttribute.IsValid is synchronous.
/// </summary>
public sealed class Menu(HttpClient http)
{
    public IReadOnlyList<MenuEntry> Items { get; private set; } = [];

    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Items = await http.GetFromJsonAsync<List<MenuEntry>>("menu", cancellationToken) ?? [];

    public MenuEntry? Find(string id) => Items.FirstOrDefault(i => i.Id == id);
}

/// <summary>The value must be the id of a dish on the caterer's current menu.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class OnMenuAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var menu = (Menu?)context.GetService(typeof(Menu))
            ?? throw new InvalidOperationException("[OnMenu] needs the Menu service in the validation context.");

        return menu.Find(value.ToString()!) is not null
            ? ValidationResult.Success
            : new ValidationResult(
                $"'{value}' is not on the menu. Choose one of: {string.Join(", ", menu.Items.Select(i => i.Id))}.",
                context.MemberName is { } member ? [member] : null);
    }
}
