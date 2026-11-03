using System;
using System.Collections.Generic;
using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Replaces a recipe's single step with an accepted split: the first row keeps its identity and
    /// takes the first step, the rest are appended in order. Only text that passed
    /// <see cref="RecipeRepairGrounding.AcceptSplit"/> belongs here.
    /// </summary>
    public static class RecipeStepSplit
    {
        public static void Apply(RecipeEntity recipe, RecipeStepEntity first, IReadOnlyList<string> steps)
        {
            var now = DateTime.UtcNow;
            first.Description = steps[0];
            first.Summary = Clamp(steps[0], 255);
            first.LastModifiedDate = now;
            first.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            recipe.RecipeSteps ??= new List<RecipeStepEntity>();
            for (var i = 1; i < steps.Count; i++)
            {
                recipe.RecipeSteps.Add(new RecipeStepEntity
                {
                    RecipeId = recipe.Id,
                    StepNumber = i + 1,
                    StepTypeId = first.StepTypeId,
                    Summary = Clamp(steps[i], 255),
                    Description = steps[i],
                    CreatedDate = now,
                    CreatedByPersonId = SystemConstants.SystemPersonId,
                });
            }
        }

        public static string Clamp(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
    }
}
