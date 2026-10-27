#:project ../src/ToolValidation
// Step 2: a validation attribute. AIFunctionFactory copies it into the schema, and that's all it does.
using Microsoft.Extensions.AI;
using ToolValidation;

var cakeOrDeath = AIFunctionFactory.Create(RegexStage.CakeOrDeath, new AIFunctionFactoryOptions { Name = "cake_or_death" });

// [RegularExpression("^(cake|death)$")] shows up as "pattern".
Demo.Show("What the model sees", cakeOrDeath.JsonSchema);

Demo.Show("cake_or_death(cake)", await Demo.WhatTheModelGets(cakeOrDeath, new { choice = "cake" }));

// "Or chicken?" Nothing checks the pattern, so your method gets it.
Demo.Show("cake_or_death(chicken)", await Demo.WhatTheModelGets(cakeOrDeath, new { choice = "chicken" }));
