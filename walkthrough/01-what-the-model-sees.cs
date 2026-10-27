#:project ../src/ToolValidation
// Step 1: a vanilla Agent Framework tool with the menu as an enum.
using Microsoft.Extensions.AI;
using ToolValidation;

var orderCatering = AIFunctionFactory.Create(EnumStage.OrderCatering, new AIFunctionFactoryOptions { Name = "order_catering" });

// This is all the model knows about the tool. The enum names are the only "display" it gets.
Demo.Show("What the model sees", orderCatering.JsonSchema);

// A good call.
Demo.Show("order_catering(FruitPlatter, 4)",
    await Demo.WhatTheModelGets(orderCatering, new { item = "FruitPlatter", quantity = 4 }));

// A value that isn't in the enum. Binding fails before your code runs, and this is all the model is told.
Demo.Show("order_catering(ChocolateFountain, 1)",
    await Demo.WhatTheModelGets(orderCatering, new { item = "ChocolateFountain", quantity = 1 }));
