using Nom.Orch.Interfaces;
using Nom.Orch.Models.Question;
using Nom.Orch.Enums.Question; // For AnswerTypeEnum
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Question; // For QuestionEntity, AnswerEntity
using Nom.Data.Reference; // For ReferenceDiscriminatorEnum, ReferenceEntity
using Nom.Data.Plan; // For RestrictionEntity (lives in Plan namespace)
using Microsoft.EntityFrameworkCore; // For ToListAsync(), FirstOrDefaultAsync(), AnyAsync()
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System; // For Console.WriteLine and JsonException
using System.Text.Json; // For JsonSerializer

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the business logic for managing questions and answers.
    /// Provides methods for fetching onboarding questions, submitting answers, and inferring dietary restrictions.
    /// </summary>
    public class QuestionOrchestrationService : IQuestionOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;

        /// <summary>
        /// Initializes a new instance of the QuestionOrchestrationService class.
        /// </summary>
        /// <param name="dbContext">The database context for accessing question and answer entities.</param>
        public QuestionOrchestrationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Retrieves a list of questions required for initial user onboarding or plan creation.
        /// </summary>
        /// <returns>A list of QuestionOrchestrationModel containing relevant questions.</returns>
        public async Task<List<QuestionOrchestrationModel>> GetRequiredOnboardingQuestionsAsync()
        {
            var questions = await _dbContext.Questions
                                            .OrderBy(q => q.DisplayOrder)
                                            .ToListAsync();

            var questionModels = new List<QuestionOrchestrationModel>();

            foreach (var question in questions)
            {
                var answerTypeEnum = MapReferenceIdToAnswerTypeEnum(question.AnswerTypeRefId);

                questionModels.Add(new QuestionOrchestrationModel
                {
                    Id = question.Id,
                    Text = question.Text,
                    Hint = question.Hint,
                    QuestionCategoryId = question.QuestionCategoryId,
                    AnswerType = answerTypeEnum,
                    DisplayOrder = question.DisplayOrder,
                    IsActive = question.IsActive,
                    DefaultAnswer = question.DefaultAnswer,
                    ValidationRegex = question.ValidationRegex,
                    Options = ParseJsonStringToList(question.Options)
                });
            }

            return questionModels;
        }

        /// <summary>
        /// Retrieves a list of restriction assignment questions.
        /// </summary>
        /// <returns>A list of QuestionOrchestrationModel containing restriction assignment questions.</returns>
        public async Task<List<QuestionOrchestrationModel>> GetRestrictionAssignmentQuestionsAsync()
        {
            var questions = await _dbContext.Questions
                                            .Where(q => q.Id >= 28L && q.Id <= 30L) // Fetch restriction assignment questions
                                            .OrderBy(q => q.DisplayOrder)
                                            .ToListAsync();

            var questionModels = new List<QuestionOrchestrationModel>();

            foreach (var question in questions)
            {
                var answerTypeEnum = MapReferenceIdToAnswerTypeEnum(question.AnswerTypeRefId);

                questionModels.Add(new QuestionOrchestrationModel
                {
                    Id = question.Id,
                    Text = question.Text,
                    Hint = question.Hint,
                    QuestionCategoryId = question.QuestionCategoryId,
                    AnswerType = answerTypeEnum,
                    DisplayOrder = question.DisplayOrder,
                    IsActive = question.IsActive,
                    DefaultAnswer = question.DefaultAnswer,
                    ValidationRegex = question.ValidationRegex,
                    Options = ParseJsonStringToList(question.Options)
                });
            }

            return questionModels;
        }

        /// <summary>
        /// Submits and processes a collection of answers for onboarding questions.
        /// Performs validation against question types and stores the answers.
        /// Also infers and creates RestrictionEntity records based on specific answers.
        /// </summary>
        /// <param name="personId">The ID of the person submitting the answers.</param>
        /// <param name="answers">A list of AnswerOrchestrationModel containing the question ID and submitted answer.</param>
        /// <returns>True if all answers were successfully processed and saved; otherwise, false.</returns>
        public async Task<bool> SubmitOnboardingAnswersAsync(long personId, List<AnswerOrchestrationModel> answers)
        {
            if (answers == null || !answers.Any())
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

            // Get the ID of the "System" person, which is typically 1L from CustomMigration seeding
            var systemPerson = await _dbContext.Persons
                                                 .FirstOrDefaultAsync(p => p.Name == "System");
            var systemPersonId = systemPerson == null ? 0 : systemPerson.Id;

            if (systemPersonId == 0) // Should be 1L or greater if found, 0 if not found (default for long)
            {
                Console.WriteLine("SubmitOnboardingAnswers: 'System' person not found for auditing restrictions. Please ensure it's seeded.");
                return false;
            }

            var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
            var questions = await _dbContext.Questions
                                            .Where(q => questionIds.Contains(q.Id))
                                            .ToDictionaryAsync(q => q.Id);

            var answersToSave = new List<AnswerEntity>();
            var restrictionsToSave = new List<RestrictionEntity>();

            foreach (var submittedAnswer in answers)
            {
                if (!questions.TryGetValue(submittedAnswer.QuestionId, out var question))
                {
                    Console.WriteLine($"SubmitOnboardingAnswers: Question with ID {submittedAnswer.QuestionId} not found for submitted answer.");
                    continue;
                }

                var answerType = MapReferenceIdToAnswerTypeEnum(question.AnswerTypeRefId);
                string? processedAnswerValue = submittedAnswer.SubmittedAnswer;

                // --- Standard Validation Logic ---
                switch (answerType)
                {
                    case AnswerTypeEnum.YesNo:
                        if (!string.Equals(processedAnswerValue, "true", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(processedAnswerValue, "false", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"SubmitOnboardingAnswers: Invalid answer for Yes/No question {question.Id}: '{processedAnswerValue}'");
                            continue;
                        }
                        break;
                    case AnswerTypeEnum.TextInput:
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
                    case AnswerTypeEnum.MultiSelect:
                    case AnswerTypeEnum.SingleSelect:
                        if (!string.IsNullOrWhiteSpace(processedAnswerValue))
                        {
                            try
                            {
                                var deserializedList = JsonSerializer.Deserialize<List<string>>(processedAnswerValue);
                                if (deserializedList == null || !deserializedList.Any())
                                {
                                    Console.WriteLine($"SubmitOnboardingAnswers: Required multi/single select question {question.Id} has no valid selections.");
                                    continue;
                                }
                            }
                            catch (JsonException ex)
                            {
                                Console.WriteLine($"SubmitOnboardingAnswers: Invalid JSON format for select question {question.Id}: {processedAnswerValue}. Error: {ex.Message}");
                                continue;
                            }
                        }
                        break;
                    default:
                        Console.WriteLine($"SubmitOnboardingAnswers: Unhandled answer type for question {question.Id}: {answerType}");
                        break;
                }
                // --- End Standard Validation Logic ---

                // Restriction Inference Logic
                await InferAndAddRestrictions(
                    question.Id,
                    processedAnswerValue,
                    personId,
                    systemPersonId, // The "System" who is recording this restriction
                    restrictionsToSave
                );

                // Save the answer entity itself
                answersToSave.Add(new AnswerEntity
                {
                    QuestionId = question.Id,
                    AnswerText = processedAnswerValue ?? string.Empty,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = systemPersonId // The "System" person who submitted the answer
                });
            }

            if (answersToSave.Any())
            {
                _dbContext.Answers.AddRange(answersToSave);
            }

            if (restrictionsToSave.Any())
            {
                var distinctRestrictions = restrictionsToSave
                    .GroupBy(r => new { r.PersonId, r.RestrictionTypeId, r.PlanId })
                    .Select(g => g.First())
                    .ToList();

                foreach (var restriction in distinctRestrictions)
                {
                    var exists = await _dbContext.Restrictions
                        .AnyAsync(r => r.RestrictionTypeId == restriction.RestrictionTypeId &&
                                       r.PersonId == restriction.PersonId &&
                                       r.PlanId == restriction.PlanId);

                    if (!exists)
                    {
                        _dbContext.Restrictions.Add(restriction);
                    }
                    else
                    {
                         Console.WriteLine($"INFO: Skipping adding duplicate restriction: '{restriction.Name}' for PersonId: {restriction.PersonId?.ToString() ?? "N/A"} and PlanId: {restriction.PlanId?.ToString() ?? "N/A"}.");
                    }
                }
            }

            try
            {
                await _dbContext.SaveChangesAsync();
                Console.WriteLine($"SubmitOnboardingAnswers: Successfully processed and saved answers and inferred restrictions for Person ID {personId}.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitOnboardingAnswers: Error saving answers and restrictions for Person ID {personId}: {ex.Message}");
                // Log the full exception details if needed for debugging
                return false;
            }
        }

        /// <summary>
        /// Submits and processes a collection of answers for restriction assignment questions.
        /// </summary>
        /// <param name="planId">The ID of the plan submitting the answers.</param>
        /// <param name="answers">A list of AnswerOrchestrationModel containing the question ID and submitted answer.</param>
        /// <returns>True if all answers were successfully processed and saved; otherwise, false.</returns>
        public async Task<bool> SubmitRestrictionAssignmentAnswersAsync(long planId, List<AnswerOrchestrationModel> answers)
        {
            if (planId <= 0 || answers == null || !answers.Any())
            {
                Console.WriteLine("SubmitRestrictionAssignmentAnswers: Invalid planId or no answers provided.");
                return false;
            }

            var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.Id == planId);
            if (plan == null)
            {
                Console.WriteLine($"SubmitRestrictionAssignmentAnswers: Plan with ID {planId} not found.");
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
                    Console.WriteLine($"SubmitRestrictionAssignmentAnswers: Question with ID {submittedAnswer.QuestionId} not found for submitted answer.");
                    continue;
                }

                var answerType = MapReferenceIdToAnswerTypeEnum(question.AnswerTypeRefId);
                string? processedAnswerValue = submittedAnswer.SubmittedAnswer;
                    answersToSave.Add(new AnswerEntity
                    {
                        QuestionId = question.Id,
                        AnswerText = processedAnswerValue ?? String.Empty,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = plan.CreatedByPersonId // The person who created the plan
                    });

            }

            if (answersToSave.Any())
            {
                _dbContext.Answers.AddRange(answersToSave);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
                Console.WriteLine($"SubmitRestrictionAssignmentAnswers: Successfully processed and saved answers for Plan ID {planId}.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitRestrictionAssignmentAnswers: Error saving answers for Plan ID {planId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Maps a reference ID to its corresponding AnswerTypeEnum value.
        /// </summary>
        /// <param name="referenceId">The reference ID to map.</param>
        /// <returns>The corresponding AnswerTypeEnum value.</returns>
        private AnswerTypeEnum MapReferenceIdToAnswerTypeEnum(long referenceId)
        {
            return referenceId switch
            {
                1000L => AnswerTypeEnum.YesNo,
                1001L => AnswerTypeEnum.TextInput,
                1002L => AnswerTypeEnum.MultiSelect,
                1003L => AnswerTypeEnum.SingleSelect,
                _ => AnswerTypeEnum.Unknown,
            };
        }

        /// <summary>
        /// Parses a JSON string into a list of strings.
        /// </summary>
        /// <param name="jsonString">The JSON string to parse.</param>
        /// <returns>A list of strings, or null if parsing fails.</returns>
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

        /// <summary>
        /// Infers and adds dietary restrictions based on question answers.
        /// </summary>
        /// <param name="questionId">The ID of the question being answered.</param>
        /// <param name="answerValue">The value of the answer provided.</param>
        /// <param name="personId">The ID of the person associated with the answer.</param>
        /// <param name="createdByPersonId">The ID of the person creating the restrictions.</param>
        /// <param name="restrictionsToSave">The list of restrictions to save.</param>
        private async Task InferAndAddRestrictions(
            long questionId,
            string? answerValue,
            long personId,
            long createdByPersonId,
            List<RestrictionEntity> restrictionsToSave)
        {
            if (string.IsNullOrWhiteSpace(answerValue))
            {
                return; // No answer, no restriction to infer
            }

            // A restriction must be tied to at least a person OR a plan.
            // For onboarding, we are initially only linking to a person (PlanId will be null here).
            if (personId == 0) // Or (personId == null && planId == null) if planId was passed in
            {
                Console.WriteLine($"InferRestrictions: Cannot add restriction without a PersonId (or PlanId if applicable). QuestionId: {questionId}");
                return;
            }

            // Dictionary to map question 5 answers to RestrictionType names
            var dietaryFoundationMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"Kosher", "Kosher"},
                {"Halal", "Halal"},
                {"Vegetarian", "Vegetarian"},
                {"Vegan", "Vegan"},
                {"Pescatarian", "Pescatarian"},
                {"Paleo", "Paleo"},
                {"Keto", "Keto"},
                {"Mediterranean", "Mediterranean"},
                {"Dash Diet", "Dash Diet"}
            };

            // Dictionary to map question 8 answers to RestrictionType names
            var allergyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"Peanuts", "Nut Allergy"},
                {"Tree Nuts", "Nut Allergy"},
                {"Dairy", "Dairy-Free"},
                {"Eggs", "Egg Allergy"},
                {"Soy", "Soy Allergy"},
                {"Wheat", "Gluten-Free"},
                {"Fish", "Fish Allergy"},
                {"Shellfish", "Shellfish Allergy"},
                {"Sesame", "Sesame Allergy"},
                {"Corn", "Corn Allergy"},
                {"Sulfites", "Sulfites Sensitivity"}
            };

            switch (questionId)
            {
                case 5L: // "Which of the following dietary foundations apply to anyone participating?" (Multi-Select)
                    var selectedFoundations = ParseJsonStringToList(answerValue);
                    if (selectedFoundations != null)
                    {
                        foreach (var foundation in selectedFoundations)
                        {
                            if (dietaryFoundationMap.TryGetValue(foundation, out var restrictionTypeName))
                            {
                                await AddRestrictionIfNotFound(
                                    questionId,
                                    personId,
                                    null, // PlanId is null for onboarding restrictions inferred this way
                                    restrictionTypeName,
                                    createdByPersonId,
                                    restrictionsToSave
                                );
                            }
                            else
                            {
                                Console.WriteLine($"InferRestrictions: Unmapped dietary foundation selected: {foundation} for QuestionId: {questionId}");
                            }
                        }
                    }
                    break;

                case 8L: // "Please indicate any diagnosed food allergies for participants:" (Multi-Select)
                    var selectedAllergies = ParseJsonStringToList(answerValue);
                    if (selectedAllergies != null)
                    {
                        foreach (var allergy in selectedAllergies)
                        {
                            if (allergyMap.TryGetValue(allergy, out var restrictionTypeName))
                            {
                                await AddRestrictionIfNotFound(
                                    questionId,
                                    personId,
                                    null, // PlanId is null for onboarding restrictions inferred this way
                                    restrictionTypeName,
                                    createdByPersonId,
                                    restrictionsToSave
                                );
                            }
                            else
                            {
                                Console.WriteLine($"InferRestrictions: Unmapped allergy selected: {allergy} for QuestionId: {questionId}");
                            }
                        }
                    }
                    break;

                case 9L: // "Is anyone managing Gluten Sensitivity or Celiac Disease?" (Yes/No)
                    if (string.Equals(answerValue, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        await AddRestrictionIfNotFound(
                            questionId,
                            personId,
                            null, // PlanId is null for onboarding restrictions inferred this way
                            "Gluten-Free",
                            createdByPersonId,
                            restrictionsToSave
                        );
                    }
                    break;

                case 10L: // "Is anyone managing Lactose Intolerance?" (Yes/No)
                    if (string.Equals(answerValue, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        await AddRestrictionIfNotFound(
                            questionId,
                            personId,
                            null, // PlanId is null for onboarding restrictions inferred this way
                            "Lactose-Intolerant",
                            createdByPersonId,
                            restrictionsToSave
                        );
                    }
                    break;
                // Add other restriction inference cases here as needed
            }
        }

        /// <summary>
        /// Helper to add a RestrictionEntity if it doesn't already exist for the given person/plan and type.
        /// </summary>
        /// <param name="questionId">The ID of the question being answered.</param>
        /// <param name="personId">The ID of the person associated with the restriction.</param>
        /// <param name="planId">The ID of the plan associated with the restriction, if applicable.</param>
        /// <param name="restrictionTypeName">The name of the restriction type.</param>
        /// <param name="createdByPersonId">The ID of the person creating the restriction.</param>
        /// <param name="restrictionsToSave">The list of restrictions to save.</param>
        private async Task AddRestrictionIfNotFound(
            long questionId,
            long personId,
            long? planId,
            string restrictionTypeName,
            long createdByPersonId,
            List<RestrictionEntity> restrictionsToSave)
        {
            var restrictionTypeRefId = await GetReferenceIdByNameAsync(restrictionTypeName, (long)ReferenceDiscriminatorEnum.RestrictionType);

            if (restrictionTypeRefId == 0)
            {
                Console.WriteLine($"Restriction type '{restrictionTypeName}' not found in Reference data for Group 'RestrictionType'. Cannot add restriction. QuestionId: {questionId}");
                return;
            }

            // Check if this restriction already exists for this person/plan combination
            var existingRestriction = await _dbContext.Restrictions
                .AnyAsync(r => r.RestrictionTypeId == restrictionTypeRefId && r.PersonId == personId && r.PlanId == planId);

            if (!existingRestriction)
            {
                restrictionsToSave.Add(new RestrictionEntity
                {
                    PersonId = personId,
                    PlanId = planId,
                    Name = restrictionTypeName,
                    Description = $"Automatically inferred from onboarding questions (Q{questionId}).",
                    RestrictionTypeId = restrictionTypeRefId,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = createdByPersonId
                });
                Console.WriteLine($"Inferred and added restriction: '{restrictionTypeName}' for Person ID {personId} and Plan ID {planId?.ToString() ?? "N/A"}.");
            }
            else
            {
                Console.WriteLine($"Restriction: '{restrictionTypeName}' already exists for Person ID {personId} and Plan ID {planId?.ToString() ?? "N/A"}. Skipping.");
            }
        }

        /// <summary>
        /// Retrieves the Id of a ReferenceEntity by its Name and GroupId.
        /// </summary>
        /// <param name="name">The name of the reference entity.</param>
        /// <param name="groupId">The ID of the reference group.</param>
        /// <returns>The ID of the reference entity, or 0 if not found.</returns>
        private async Task<long> GetReferenceIdByNameAsync(string name, long groupId)
        {
            // Corrected query: Now that ReferenceEntity.Groups is correctly configured for many-to-many,
            // we can use .Any() on the navigation property directly.
            var referenceId = await _dbContext.References
                .Where(r => r.Name == name && r.Groups != null && r.Groups.Any(g => g.Id == groupId))
                .Select(r => r.Id)
                .FirstOrDefaultAsync();

            return referenceId;
        }
    }
}
