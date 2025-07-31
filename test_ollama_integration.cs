using System;
using System.Threading.Tasks;
using Nom.Import.Services;

namespace Nom.Import
{
    public class TestOllamaIntegration
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Testing Ollama Integration...");
            
            try
            {
                // Test the Ollama service directly
                var httpClient = new System.Net.Http.HttpClient();
                var ollamaService = new OllamaService(httpClient, null);
                
                Console.WriteLine("Testing Ollama connection...");
                
                var testPrompt = @"You are a nutrition data specialist. The ehancedName should be as simple as possible without inferring additional non-supplied details. The description should be comprehensive, but not add any non-inferrable details. Enhance this ingredient data:. If any brand names are in the original, they can be placed in the aliases, but should not be included in the produced Name or Description. 

Original: Tomato, red, ripe, raw - Red tomato. Aliases: tomato, red tomato, ripe tomato, raw tomato.
Original: Chicken breast, raw - Chicken breast. Aliases: chicken breast, raw chicken breast, white chicken meat, chicken breast meat.
Original: Kellog's Frosted Corn Flakes - Frosted Corn Flakes. Aliases: Kellog's Frosted Flakes, Frosted Corn Flakes, Kellog's Corn Flakes, Corn Flakes.

Provide a JSON response with enhancedName, enhancedDescription, and aliases array.";

                var response = await ollamaService.ProcessPromptAsync(testPrompt);
                
                Console.WriteLine("Ollama Response:");
                Console.WriteLine(response);
                
                Console.WriteLine("\n✅ Ollama integration test successful!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ollama integration test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
} 