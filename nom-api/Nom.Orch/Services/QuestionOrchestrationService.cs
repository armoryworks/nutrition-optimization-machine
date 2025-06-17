// Nom.Orch/Services/QuestionOrchestrationService.cs
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Question;
using Nom.Orch.Enums; // ADDED: To use AnswerTypeEnum
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Question; // For QuestionEntity, AnswerEntity
using Nom.Data.Reference; // For ReferenceDiscriminatorEnum (still needed for initial data mapping)
using Microsoft.EntityFrameworkCore; // For ToListAsync(), FirstOrDefaultAsync()
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System; // For Console.WriteLine and JsonException
using System.Text.Json;
using Nom.Orch.Enums.Question; // For JsonSerializer

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the business logic for managing questions and answers,
    /// primarily focused on fetching onboarding questions and submitting answers.
    /// </summary>
    public class QuestionOrchestrationService : IQuestionOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;

        public QuestionOrchestrationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Retrieves a list of questions that are required for initial user onboarding or plan creation.
        /// </summary>
        /// <returns>A list of QuestionOrchestrationModel containing relevant questions.</returns>
        public async Task<List<QuestionOrchestrationModel>> GetRequiredOnboardingQuestionsAsync()
        {
            // Fetch questions from the database that are marked as required for plan creation.
            // Order them as defined by the DisplayOrder.
            var questions = await _dbContext.Questions
                                            .Where(q => q.IsRequiredForPlanCreation)
                                            .OrderBy(q => q.DisplayOrder)
                                            .ToListAsync();

            var questionModels = new List<QuestionOrchestrationModel>();

            foreach (var question in questions)
            {
                // Map the AnswerTypeRefId (from Nom.Data.Reference) to the internal AnswerTypeEnum
                var answerTypeEnum = MapReferenceIdToAnswerTypeEnum(question.AnswerTypeRefId);

                questionModels.Add(new QuestionOrchestrationModel
                {
                    Id = question.Id,
                    Text = question.Text,
                    Hint = question.Hint,
                    QuestionCategoryId = question.QuestionCategoryId,
                    AnswerType = answerTypeEnum, // Now uses AnswerTypeEnum
                    DisplayOrder = question.DisplayOrder,
                    IsActive = question.IsActive,
                    IsRequiredForPlanCreation = question.IsRequiredForPlanCreation,
                    DefaultAnswer = question.DefaultAnswer,
                    ValidationRegex = question.ValidationRegex,
                    Options = ParseJsonStringToList(question.DefaultAnswer)
                });
            }

            return questionModels;
        }

        /// <summary>
        /// Submits and processes a collection of answers for a given person.
        /// Performs validation against question types and stores the answers.
        /// </summary>
        /// <param name="personId">The ID of the person submitting the answers.</param>
        /// <param name="answers">A list of AnswerOrchestrationModel containing the question ID and submitted answer.</param>
        /// <returns>True if all answers were successfully processed and saved; otherwise, false.</returns>
        public async Task<bool> SubmitOnboardingAnswersAsync(long personId, List<AnswerOrchestrationModel> answers)
        {
            if (personId <= 0 || answers == null || !answers.Any())
            {
                Console.WriteLine("SubmitOnboardingAnswers: Invalid personId or no answers provided.");
                return false;
            }

            var person = await _dbContext.Persons.FirstOrDefaultAsync(p => p.Id == personId);
            if (person == null)
            {
                Console.WriteLine($"SubmitOnboardingAnswers: Person with ID {personId} not found.");
                return false;
            }

            var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
            var questions = await _dbContext.Questions
                                            .Where(q => questionIds.Contains(q.Id))
                                            .ToDictionaryAsync(q => q.Id);

            var answersToSave = new List<AnswerEntity>();

            foreach (var submittedAnswer in answers)
            {
                if (!questions.TryGetValue(submittedAnswer.QuestionId, out var question))
                {
                    Console.WriteLine($"SubmitOnboardingAnswers: Question with ID {submittedAnswer.QuestionId} not found for submitted answer.");
                    continue;
                }

                // Map AnswerTypeRefId to the internal AnswerTypeEnum for validation logic
                var answerType = MapReferenceIdToAnswerTypeEnum(question.AnswerTypeRefId);
                string? processedAnswerValue = submittedAnswer.SubmittedAnswer;

                // --- Validation logic based on AnswerTypeEnum ---
                switch (answerType)
                {
                    case AnswerTypeEnum.YesNo: // Using new enum
                        if (!string.Equals(processedAnswerValue, "true", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(processedAnswerValue, "false", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"SubmitOnboardingAnswers: Invalid answer for Yes/No question {question.Id}: '{processedAnswerValue}'");
                            continue;
                        }
                        break;
                    case AnswerTypeEnum.TextInput: // Using new enum
                        if (question.IsRequiredForPlanCreation && string.IsNullOrWhiteSpace(processedAnswerValue))
                        {
                            Console.WriteLine($"SubmitOnboardingAnswers: Required text input question {question.Id} has no answer.");
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(question.ValidationRegex) && !string.IsNullOrWhiteSpace(processedAnswerValue))
                        {
                            try
                            {
                                if (!System.Text.RegularExpressions.Regex.IsMatch(processedAnswerValue, question.ValidationRegex))
                                {
                                    Console.WriteLine($"SubmitOnboardingAnswers: Validation regex failed for question {question.Id}: '{processedAnswerValue}'");
                                    continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"SubmitOnboardingAnswers: Error with regex validation for question {question.Id}: {ex.Message}");
                                continue;
                            }
                        }
                        break;
                    case AnswerTypeEnum.MultiSelect: // Using new enum
                    case AnswerTypeEnum.SingleSelect: // Using new enum
                        if (!string.IsNullOrWhiteSpace(processedAnswerValue))
                        {
                            try
                            {
                                var deserializedList = JsonSerializer.Deserialize<List<string>>(processedAnswerValue);
                                if (deserializedList == null || !deserializedList.Any())
                                {
                                    if (question.IsRequiredForPlanCreation)
                                    {
                                        Console.WriteLine($"SubmitOnboardingAnswers: Required multi/single select question {question.Id} has no valid selections.");
                                        continue;
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                Console.WriteLine($"SubmitOnboardingAnswers: Invalid JSON format for select question {question.Id}: {processedAnswerValue}. Error: {ex.Message}");
                                continue;
                            }
                        }
                        else if (question.IsRequiredForPlanCreation)
                        {
                            Console.WriteLine($"SubmitOnboardingAnswers: Required multi/single select question {question.Id} has no answer provided.");
                            continue;
                        }
                        break;
                    default:
                        Console.WriteLine($"SubmitOnboardingAnswers: Unhandled answer type for question {question.Id}: {answerType}");
                        break;
                }

                // Check if an answer for this person and question already exists
                var existingAnswer = await _dbContext.Answers
                                                     .FirstOrDefaultAsync(a => a.PersonId == personId && a.QuestionId == question.Id);

                if (existingAnswer != null)
                {
                    // --- UPDATED: Use AnswerText property ---
                    existingAnswer.AnswerText = processedAnswerValue??String.Empty;
                }
                else
                {
                    // --- UPDATED: Use AnswerText and new audit fields ---
                    answersToSave.Add(new AnswerEntity
                    {
                        QuestionId = question.Id,
                        PersonId = personId,
                        PlanId = null, // Onboarding answers not tied to a plan initially
                        AnswerText = processedAnswerValue??String.Empty, // Use AnswerText
                        CreatedDate = DateTime.UtcNow, // Set CreatedDate
                        CreatedByPersonId = person.Id // The person who submitted the answer
                    });
                }
            }

            if (answersToSave.Any())
            {
                _dbContext.Answers.AddRange(answersToSave);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
                Console.WriteLine($"SubmitOnboardingAnswers: Successfully processed and saved answers for Person ID {personId}.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitOnboardingAnswers: Error saving answers for Person ID {personId}: {ex.Message}");
                // Log the full exception details
                return false;
            }
        }

        /// <summary>
        /// Helper method to map a Reference.Id (from QuestionEntity.AnswerTypeRefId) to AnswerTypeEnum.
        /// </summary>
        /// <param name="referenceId">The ID from the Reference table (e.g., 1000L for Yes/No).</param>
        /// <returns>The corresponding AnswerTypeEnum value.</returns>
        private AnswerTypeEnum MapReferenceIdToAnswerTypeEnum(long referenceId)
        {
            // These IDs come from CustomMigration.AddAnswerTypes
            return referenceId switch
            {
                1000L => AnswerTypeEnum.YesNo,
                1001L => AnswerTypeEnum.TextInput,
                1002L => AnswerTypeEnum.MultiSelect,
                1003L => AnswerTypeEnum.SingleSelect,
                _ => AnswerTypeEnum.Unknown, // Handle unexpected IDs
            };
        }

        /// <summary>
        /// Helper method to parse a JSON string into a List<string>.
        /// </summary>
        private List<string>? ParseJsonStringToList(string? jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                return null;
            }
            try
            {
                return JsonSerializer.Deserialize<List<string>>(jsonString);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing JSON string '{jsonString}': {ex.Message}");
                return null;
            }
        }
    }
}