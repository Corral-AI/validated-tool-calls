using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ToolValidation;

/// <summary>Helpers that keep the walkthrough steps short.</summary>
public static class Demo
{
    private static readonly JsonSerializerOptions Pretty = new(AIJsonUtilities.DefaultOptions) { WriteIndented = true };

    /// <summary>Prints a heading and the value as indented JSON.</summary>
    public static void Show(string title, object? value)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n── {title}");
        Console.ResetColor();
        Console.WriteLine(value is string s ? s : JsonSerializer.Serialize(value, Pretty));
    }

    /// <summary>
    /// Has a scripted model call <paramref name="function"/> once with <paramref name="arguments"/>,
    /// through the real FunctionInvokingChatClient, and returns exactly what the model is sent back.
    /// </summary>
    public static async Task<object?> WhatTheModelGets(AIFunction function, object arguments)
    {
        using var client = new FunctionInvokingChatClient(new ScriptedChatClient(ScriptedChatClient.Call(function.Name, arguments)));
        var response = await client.GetResponseAsync("Go.", new ChatOptions { Tools = [function] });

        var result = response.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last().Result;
        return result is JsonElement json ? json : result?.ToString();
    }
}
