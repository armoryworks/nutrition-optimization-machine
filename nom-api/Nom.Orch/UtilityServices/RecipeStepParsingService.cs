// Nom.Orch/Services/RecipeStepParsingService.cs
using Nom.Orch.UtilityInterfaces;
using Nom.Data.Recipe; // For RecipeStepEntity
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System;

namespace Nom.Orch.UtilityServices
{
    /// <summary>
    /// Service responsible for parsing raw recipe instructions into structured steps.
    /// This implementation uses basic text splitting and numbering.
    /// </summary>
    public class RecipeStepParsingService : IRecipeStepParsingService
    {
        private readonly ILogger<RecipeStepParsingService> _logger;

        public RecipeStepParsingService(ILogger<RecipeStepParsingService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Parses a raw, multi-step instruction string into an ordered list of RecipeStepEntity objects.
        /// This implementation attempts to split instructions by common patterns like numbered lists,
        /// or by sentences/paragraphs if no explicit numbering is found.
        /// </summary>
        /// <param name="rawInstructions">The complete raw instruction string from a recipe.</param>
        /// <returns>A list of parsed and ordered RecipeStepEntity objects.</returns>
        public Task<List<RecipeStepEntity>> ParseInstructionsIntoStepsAsync(string rawInstructions)
        {
            var steps = new List<RecipeStepEntity>();

            if (string.IsNullOrWhiteSpace(rawInstructions))
            {
                return Task.FromResult(steps);
            }

            // Strategy 1: Look for numbered lists (e.g., "1.", "2)", "Step 3:")
            var numberedSteps = Regex.Matches(rawInstructions, @"(\d+[\.\)]|\bStep\s*\d+[:\)])\s*(.*?)(?=(\d+[\.\)]|\bStep\s*\d+[:\)]|$))", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                                     .Cast<Match>()
                                     .Select(m => m.Groups[2].Value.Trim())
                                     .Where(s => !string.IsNullOrWhiteSpace(s))
                                     .ToList();

            if (numberedSteps.Any())
            {
                byte stepNumber = 1;
                foreach (var stepText in numberedSteps)
                {
                    steps.Add(new RecipeStepEntity
                    {
                        StepNumber = stepNumber++,
                        Summary = TruncateString(stepText, 255), // Take first part as summary
                        Description = stepText
                    });
                }
            }
            else
            {
                // Strategy 2: Split by common sentence/paragraph delimiters if no numbering is found.
                // This is a very simple approach and can be greatly improved with NLP libraries.
                var paragraphSteps = rawInstructions.Split(new[] { Environment.NewLine + Environment.NewLine, ".", "?", "!" }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(s => s.Trim())
                                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                                    .ToList();

                // If splitting by sentences/paragraphs results in too many small steps,
                // or if it's just one large block, you might need to adjust.
                if (paragraphSteps.Count > 1 || (paragraphSteps.Count == 1 && paragraphSteps[0].Length > 50)) // Arbitrary length check
                {
                    byte stepNumber = 1;
                    foreach (var stepText in paragraphSteps)
                    {
                        if (stepNumber <= byte.MaxValue) // Prevent overflow for StepNumber
                        {
                            steps.Add(new RecipeStepEntity
                            {
                                StepNumber = stepNumber++,
                                Summary = TruncateString(stepText, 255),
                                Description = stepText
                            });
                        }
                        else
                        {
                            _logger.LogWarning("Too many steps in recipe instructions, truncating steps after {MaxSteps}.", byte.MaxValue);
                            break;
                        }
                    }
                }
                else if (paragraphSteps.Any())
                {
                    // If it's just one large paragraph or a few small ones, treat as a single step
                    steps.Add(new RecipeStepEntity
                    {
                        StepNumber = 1,
                        Summary = TruncateString(rawInstructions, 255),
                        Description = rawInstructions
                    });
                }
            }

            return Task.FromResult(steps);
        }

        /// <summary>
        /// Helper to truncate strings to a specified maximum length.
        /// </summary>
        private static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
