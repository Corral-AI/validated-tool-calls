# Validated tool calls

Microsoft.Extensions.AI copies validation attributes into the JSON schema a model sees, and never
runs them. A model that ignores `[RegularExpression]` or `[Range]` reaches your method anyway, and
a model that sends a bad enum value is told only `Error: Function failed.`

TypeScript frameworks enforce their schemas. The Vercel AI SDK validates tool input against its Zod
or JSON schema and sends failed calls back to the model, and Mastra validates tool input and output.

This repo closes the gap for Microsoft Agent Framework with
[MiniValidation](https://github.com/DamianEdwards/MiniValidation): it validates tool arguments and
results, and gives the model RFC 9457 problem details it can act on.

## What's here

| Path | What it is |
| --- | --- |
| `src/ToolValidation/ValidatingAIFunction.cs` | The wrapper: a `DelegatingAIFunction` that validates arguments before the call and the result after it. `tool.WithValidation(services)` applies it. |
| `src/ToolValidation/Stages.cs` | The tools for each stage: a menu enum, `cake_or_death` with a regex, and a menu from an API with a typed chef subagent. |
| `src/ToolValidation/Menu.cs` | The menu client and `[OnMenu]`, a validation attribute that checks values against the API at run time. |
| `src/MenuApi` | The caterer's menu as a minimal API. `POST /menu` adds a dish live. |
| `src/CateringChat` | A chat CLI against a real model that prints every tool call and what the model gets back. |
| `walkthrough/` | Five C# file-based apps, one per step, using a scripted model so they run the same every time. |

## The walkthrough

Needs the .NET 10 SDK. Start the menu API first (steps 4 and 5 use it):

```bash
dotnet run --project src/MenuApi
```

Then, from `walkthrough/`:

```bash
dotnet run 01-what-the-model-sees.cs   # the schema; a bad enum value gets "Error: Function failed."
dotnet run 02-cake-or-death.cs         # "pattern" is in the schema; "chicken" goes straight through
dotnet run 03-minivalidation.cs        # the same calls, wrapped: problem details instead
dotnet run 04-menu-from-an-api.cs      # [OnMenu] against the API; add a dish with no redeploy
dotnet run 05-subagent-output.cs       # a subagent's typed result, validated before the caller sees it
```

## The chat

Set either `OPENAI_API_KEY` (and optionally `OPENAI_MODEL`), or `AZURE_OPENAI_ENDPOINT` and
`AZURE_OPENAI_DEPLOYMENT` to use your Azure login (`az login`) against Azure OpenAI or Foundry. Then:

```bash
dotnet run --project src/CateringChat -- --menu regex --validation off
dotnet run --project src/CateringChat -- --menu api --validation on
```

`--menu` is `enum`, `regex` or `api`. `--validation` is `on` or `off`.

## What this doesn't do

Validation checks values against rules. It stops a model passing an off-menu dish or a made-up
price, and tells it how to fix the call. It does not stop a model being talked into choosing a
different valid value, and it does nothing for free-text fields, which can still carry instructions.
