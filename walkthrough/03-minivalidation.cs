#:project ../src/ToolValidation
// Step 3: the same tools, wrapped so the attributes actually run (see src/ToolValidation/ValidatingAIFunction.cs).
using Microsoft.Extensions.AI;
using ToolValidation;

var cakeOrDeath = AIFunctionFactory.Create(RegexStage.CakeOrDeath, new AIFunctionFactoryOptions { Name = "cake_or_death" })
    .WithValidation();

// The method never runs. The model gets RFC 9457 problem details it can act on.
Demo.Show("cake_or_death(chicken)", await Demo.WhatTheModelGets(cakeOrDeath, new { choice = "chicken" }));

var orderCatering = AIFunctionFactory.Create(EnumStage.OrderCatering, new AIFunctionFactoryOptions { Name = "order_catering" })
    .WithValidation();

// The enum case too: "Error: Function failed." becomes a list of what's allowed.
Demo.Show("order_catering(ChocolateFountain, 1)",
    await Demo.WhatTheModelGets(orderCatering, new { item = "ChocolateFountain", quantity = 1 }));

// The catch: both lists are compiled in. A new dish means a redeploy, and every customer gets the same menu.
