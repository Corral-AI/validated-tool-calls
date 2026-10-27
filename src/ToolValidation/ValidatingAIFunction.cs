using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using MiniValidation;

namespace ToolValidation;

/// <summary>
/// Runs the validation attributes that <see cref="AIFunctionFactory"/> only copies into the schema.
/// Invalid arguments never reach the tool: the model gets RFC 9457 problem details back instead,
/// naming each field and what is wrong with it, so it can correct the call. The tool's result is
/// checked the same way before it goes back to the model.
/// </summary>
public sealed class ValidatingAIFunction(AIFunction inner, IServiceProvider? services = null) : DelegatingAIFunction(inner)
{
    private readonly MethodInfo _method = inner.UnderlyingMethod
        ?? throw new ArgumentException("Only functions created from a .NET method can be validated.", nameof(inner));

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var errors = await ValidateArgumentsAsync(arguments);
        if (errors.Count > 0)
        {
            return Problem("The tool call has invalid arguments. Fix them and call the tool again.", errors);
        }

        var result = await base.InvokeCoreAsync(arguments, cancellationToken);

        var resultErrors = await ValidateResultAsync(result);
        if (resultErrors.Count > 0)
        {
            return Problem("The tool ran but its result failed validation, so it was not used.", resultErrors);
        }

        return result;
    }

    private async Task<Dictionary<string, string[]>> ValidateArgumentsAsync(AIFunctionArguments arguments)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var parameter in _method.GetParameters())
        {
            if (!IsModelSupplied(parameter.ParameterType))
            {
                continue;
            }

            var name = parameter.Name!;
            if (!arguments.TryGetValue(name, out var raw) || raw is null)
            {
                if (!parameter.HasDefaultValue)
                {
                    errors[name] = [$"The {name} field is required."];
                }
                continue;
            }

            // Bind the way AIFunctionFactory will, but turn a failure into a message the model can act on.
            object? value;
            try
            {
                value = raw is JsonElement json
                    ? json.Deserialize(parameter.ParameterType, JsonSerializerOptions)
                    : JsonSerializer.SerializeToElement(raw, JsonSerializerOptions).Deserialize(parameter.ParameterType, JsonSerializerOptions);
            }
            catch (JsonException)
            {
                errors[name] = [$"The value {Describe(raw)} is not a valid {Describe(parameter.ParameterType)}."];
                continue;
            }

            // Attributes on the parameter itself, such as [RegularExpression] or [Range].
            var results = new List<ValidationResult>();
            var context = new ValidationContext(value ?? new object(), services, null) { MemberName = name, DisplayName = name };
            if (!Validator.TryValidateValue(value!, context, results, parameter.GetCustomAttributes<ValidationAttribute>()))
            {
                errors[name] = [.. results.Select(r => r.ErrorMessage ?? "Invalid value.")];
                continue;
            }

            // Records and classes: MiniValidation walks their properties, nested objects and IValidatableObject.
            if (value is not null && MiniValidator.RequiresValidation(value.GetType()))
            {
                var (isValid, nested) = await MiniValidator.TryValidateAsync(value, services!, recurse: true);
                if (!isValid)
                {
                    foreach (var (key, messages) in nested)
                    {
                        errors[$"{name}.{key}"] = messages;
                    }
                }
            }
        }

        return errors;
    }

    private async Task<Dictionary<string, string[]>> ValidateResultAsync(object? result)
    {
        var returnType = UnwrapTask(_method.ReturnType);
        if (result is not JsonElement json || returnType is null || !MiniValidator.RequiresValidation(returnType))
        {
            return [];
        }

        // AIFunctionFactory has already serialised the return value, so read it back as the declared type.
        var value = json.Deserialize(returnType, JsonSerializerOptions);
        if (value is null)
        {
            return [];
        }

        var (isValid, errors) = await MiniValidator.TryValidateAsync(value, services!, recurse: true);
        return isValid ? [] : new Dictionary<string, string[]>(errors);
    }

    // Returned as JSON so the model reads exactly what you see when you print it.
    private JsonElement Problem(string detail, IDictionary<string, string[]> errors) =>
        JsonSerializer.SerializeToElement(
            new HttpValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest, Detail = detail },
            JsonSerializerOptions);

    private static bool IsModelSupplied(Type type) =>
        type != typeof(CancellationToken) && type != typeof(AIFunctionArguments) && type != typeof(IServiceProvider);

    private static Type? UnwrapTask(Type type) =>
        type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask) ? null
        : type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? type.GetGenericArguments()[0]
            : type;

    private static string Describe(object raw) => raw is JsonElement json ? json.GetRawText() : JsonSerializer.Serialize(raw);

    private static string Describe(Type type) => type.IsEnum
        ? $"{type.Name}. Allowed values: {string.Join(", ", Enum.GetNames(type))}"
        : type.Name;
}

public static class ValidatingAIFunctionExtensions
{
    /// <summary>Wraps a function so its arguments and result are validated with MiniValidation.</summary>
    public static AIFunction WithValidation(this AIFunction function, IServiceProvider? services = null) =>
        new ValidatingAIFunction(function, services);
}
