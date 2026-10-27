using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ToolValidation;

/// <summary>
/// A stand-in for a model that makes the tool calls you script, in order. Real models follow
/// schemas faithfully, so this is how the demos show what happens when one doesn't. Once the
/// script runs out it replies with the last tool result it was given, so you can see exactly
/// what a model would have read.
/// </summary>
public sealed class ScriptedChatClient(params FunctionCallContent[] calls) : IChatClient
{
    private int _next;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_next < calls.Length)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [calls[_next++]])));
        }

        var lastResult = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
        var text = lastResult is null ? "(no tool was called)" : $"The tool said: {lastResult.Result}";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    /// <summary>A scripted call to <paramref name="name"/> with the given arguments.</summary>
    public static FunctionCallContent Call(string name, object arguments) =>
        new(Guid.NewGuid().ToString("N"), name,
            JsonSerializer.SerializeToElement(arguments, AIJsonUtilities.DefaultOptions)
                .Deserialize<Dictionary<string, object?>>(AIJsonUtilities.DefaultOptions));
}
