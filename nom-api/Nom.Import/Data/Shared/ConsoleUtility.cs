using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Import.Data.Shared
{
    /// <summary>
    /// Provides utility methods for console interactions, such as prompts and countdowns.
    /// </summary>
    public class ConsoleUtility
    {
        /// <summary>
        /// Prompts the user for input with a countdown.
        /// </summary>
        /// <param name="promptMessage">The message to display to the user.</param>
        /// <param name="countdownSeconds">The number of seconds to count down.</param>
        /// <returns>The user's input, or an empty string if the countdown expires.</returns>
        public static string PromptWithCountdown(string promptMessage, int countdownSeconds)
        {
            Console.Write(promptMessage);
            string? input = null;
            var inputTask = Task.Run(() => Console.ReadLine());

            for (int i = countdownSeconds; i > 0; i--)
            {
                Console.Write($" ({i}s remaining) \r");
                if (inputTask.IsCompleted)
                {
                    input = inputTask.Result;
                    break;
                }
                Thread.Sleep(1000);
            }

            if (!inputTask.IsCompleted)
            {
                // If input was not provided within the countdown, cancel the task and return empty.
                // This might leave the task running in the background, but for a console app, it's usually fine.
                // For a more robust solution, a CancellationTokenSource would be used with inputTask.
                Console.WriteLine("\nNo input received. Continuing with default.");
                return string.Empty;
            }

            Console.WriteLine(); // New line after prompt
            return input ?? string.Empty;
        }

        /// <summary>
        /// Prompts the user for a numeric input.
        /// </summary>
        /// <param name="promptMessage">The message to display.</param>
        /// <param name="defaultValue">The default value to use if input is invalid or empty.</param>
        /// <returns>The parsed integer value.</returns>
        public static int PromptForInteger(string promptMessage, int defaultValue)
        {
            while (true)
            {
                Console.Write($"{promptMessage} (default: {defaultValue}): ");
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    return defaultValue;
                }

                if (int.TryParse(input, out int result) && result >= 0)
                {
                    return result;
                }
                Console.WriteLine("Invalid input. Please enter a non-negative integer.");
            }
        }

        /// <summary>
        /// Prompts the user for a string input.
        /// </summary>
        /// <param name="promptMessage">The message to display.</param>
        /// <param name="defaultValue">The default value to use if input is empty.</param>
        /// <returns>The string input.</returns>
        public static string PromptForString(string promptMessage, string defaultValue)
        {
            Console.Write($"{promptMessage} (default: {defaultValue}): ");
            string? input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
        }

        /// <summary>
        /// Displays a list of options and prompts the user to select one by number or name.
        /// </summary>
        /// <param name="options">An array of option names.</param>
        /// <param name="promptMessage">The message to display before the options.</param>
        /// <param name="defaultIndex">The default selected index if no input is provided.</param>
        /// <returns>The selected index (0-based).</returns>
        public static int SelectOption(string[] options, string promptMessage, int defaultIndex = 0)
        {
            Console.WriteLine(promptMessage);
            for (int i = 0; i < options.Length; i++)
            {
                Console.WriteLine($"  {i + 1}. {options[i]}");
            }

            while (true)
            {
                string input = PromptWithCountdown($"Enter your choice (1-{options.Length} or name) or 'debug' for limit: ", 10);

                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine($"No input. Starting from default stage: {options[defaultIndex]}");
                    return defaultIndex;
                }

                // Try to match by number
                if (int.TryParse(input, out int choiceNum) && choiceNum >= 1 && choiceNum <= options.Length)
                {
                    Console.WriteLine($"Starting from stage {choiceNum}: {options[choiceNum - 1]}");
                    return choiceNum - 1;
                }

                // Try to match by name (case-insensitive)
                for (int i = 0; i < options.Length; i++)
                {
                    if (options[i].Equals(input, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Starting from stage {i + 1}: {options[i]}");
                        return i;
                    }
                }

                // Special "debug" input handling
                if (input.Equals("debug", StringComparison.OrdinalIgnoreCase))
                {
                    return -1; // Special value to indicate debug mode selection
                }

                Console.WriteLine("Invalid input. Please try again.");
            }
        }
    }
}
