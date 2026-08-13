using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class RecipeAdvancedOrchestrationService : IRecipeAdvancedOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<RecipeAdvancedOrchestrationService> _logger;

        public RecipeAdvancedOrchestrationService(
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            ILogger<RecipeAdvancedOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        private long GetCurrentPersonId()
        {
            var personId = _httpContextAccessor.HttpContext?.User?.FindFirst("PersonId")?.Value;
            if (string.IsNullOrEmpty(personId) || !long.TryParse(personId, out var id))
            {
                throw new UnauthorizedAccessException("User not authenticated");
            }
            return id;
        }

        private long GetCurrentUserId()
        {
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId) || !long.TryParse(userId, out var id))
            {
                throw new UnauthorizedAccessException("User not authenticated");
            }
            return id;
        }

        // Comments
        public async Task<RecipeCommentResponseModel> CreateCommentAsync(RecipeCommentCreateModel model)
        {
            var currentUserId = GetCurrentUserId();
            var recipe = await _dbContext.Recipes
                .Include(r => r.Author)
                .FirstOrDefaultAsync(r => r.Id == model.RecipeId);

            if (recipe == null)
            {
                throw new ArgumentException("Recipe not found");
            }

            var comment = new RecipeCommentEntity
            {
                RecipeId = model.RecipeId,
                AuthorId = currentUserId,
                Comment = model.Comment,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = currentUserId
            };

            _dbContext.RecipeComments.Add(comment);
            await _dbContext.SaveChangesAsync();

            return new RecipeCommentResponseModel
            {
                Id = comment.Id,
                RecipeId = comment.RecipeId,
                AuthorId = comment.AuthorId,
                AuthorName = recipe.Author?.Name ?? "Unknown",
                Comment = comment.Comment,
                CreatedDate = comment.CreatedDate,
                LastModifiedDate = comment.LastModifiedDate
            };
        }

        public async Task<List<RecipeCommentResponseModel>> GetRecipeCommentsAsync(long recipeId)
        {
            var comments = await _dbContext.RecipeComments
                .Include(c => c.Author)
                .Include(c => c.Recipe)
                .Where(c => c.RecipeId == recipeId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync();

            return comments.Select(c => new RecipeCommentResponseModel
            {
                Id = c.Id,
                RecipeId = c.RecipeId,
                AuthorId = c.AuthorId,
                AuthorName = c.Author?.Name ?? "Unknown",
                Comment = c.Comment,
                CreatedDate = c.CreatedDate,
                LastModifiedDate = c.LastModifiedDate
            }).ToList();
        }

        public async Task<bool> DeleteCommentAsync(long commentId)
        {
            var currentUserId = GetCurrentUserId();
            var comment = await _dbContext.RecipeComments
                .FirstOrDefaultAsync(c => c.Id == commentId && c.AuthorId == currentUserId);

            if (comment == null)
            {
                return false;
            }

            _dbContext.RecipeComments.Remove(comment);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        // Ratings
        public async Task<RecipeRatingResponseModel> CreateRatingAsync(RecipeRatingCreateModel model)
        {
            var currentUserId = GetCurrentUserId();
            var recipe = await _dbContext.Recipes
                .Include(r => r.Author)
                .FirstOrDefaultAsync(r => r.Id == model.RecipeId);

            if (recipe == null)
            {
                throw new ArgumentException("Recipe not found");
            }

            // Check if user already rated this recipe
            var existingRating = await _dbContext.RecipeRatings
                .FirstOrDefaultAsync(r => r.RecipeId == model.RecipeId && r.RaterId == currentUserId);

            if (existingRating != null)
            {
                throw new InvalidOperationException("User has already rated this recipe");
            }

            var rating = new RecipeRatingEntity
            {
                RecipeId = model.RecipeId,
                RaterId = currentUserId,
                Rating = model.Rating,
                DateRated = DateTime.UtcNow
            };

            _dbContext.RecipeRatings.Add(rating);
            await _dbContext.SaveChangesAsync();

            // Update recipe average rating
            await UpdateRecipeAverageRatingAsync(model.RecipeId);

            return new RecipeRatingResponseModel
            {
                Id = rating.Id,
                RecipeId = rating.RecipeId,
                RaterId = rating.RaterId,
                RaterName = rating.Rater?.Name ?? "Unknown",
                Rating = rating.Rating,
                CreatedDate = rating.CreatedDate,
                LastModifiedDate = rating.LastModifiedDate
            };
        }

        public async Task<RecipeRatingResponseModel?> GetUserRatingAsync(long recipeId)
        {
            var currentUserId = GetCurrentUserId();
            var rating = await _dbContext.RecipeRatings
                .Include(r => r.Rater)
                .FirstOrDefaultAsync(r => r.RecipeId == recipeId && r.RaterId == currentUserId);

            if (rating == null)
            {
                return null;
            }

            return new RecipeRatingResponseModel
            {
                Id = rating.Id,
                RecipeId = rating.RecipeId,
                RaterId = rating.RaterId,
                RaterName = rating.Rater?.Name ?? "Unknown",
                Rating = rating.Rating,
                CreatedDate = rating.CreatedDate,
                LastModifiedDate = rating.LastModifiedDate
            };
        }

        public async Task<decimal> GetRecipeAverageRatingAsync(long recipeId)
        {
            var averageRating = await _dbContext.RecipeRatings
                .Where(r => r.RecipeId == recipeId)
                .AverageAsync(r => (decimal)r.Rating);

            return Math.Round(averageRating, 2);
        }

        public async Task<bool> UpdateRatingAsync(long ratingId, RecipeRatingCreateModel model)
        {
            var currentUserId = GetCurrentUserId();
            var rating = await _dbContext.RecipeRatings
                .FirstOrDefaultAsync(r => r.Id == ratingId && r.RaterId == currentUserId);

            if (rating == null)
            {
                return false;
            }

            rating.Rating = model.Rating;
            rating.DateRated = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Update recipe average rating
            await UpdateRecipeAverageRatingAsync(rating.RecipeId);

            return true;
        }

        public async Task<bool> DeleteRatingAsync(long ratingId)
        {
            var currentUserId = GetCurrentUserId();
            var rating = await _dbContext.RecipeRatings
                .FirstOrDefaultAsync(r => r.Id == ratingId && r.RaterId == currentUserId);

            if (rating == null)
            {
                return false;
            }

            var recipeId = rating.RecipeId;
            _dbContext.RecipeRatings.Remove(rating);
            await _dbContext.SaveChangesAsync();

            // Update recipe average rating
            await UpdateRecipeAverageRatingAsync(recipeId);

            return true;
        }

        private async Task UpdateRecipeAverageRatingAsync(long recipeId)
        {
            var averageRating = await _dbContext.RecipeRatings
                .Where(r => r.RecipeId == recipeId)
                .AverageAsync(r => (decimal)r.Rating);

            var recipe = await _dbContext.Recipes.FindAsync(recipeId);
            if (recipe != null)
            {
                recipe.Rating = Math.Round(averageRating, 2);
                recipe.LastModifiedDate = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }

        // Share Tokens
        public async Task<RecipeShareTokenResponseModel> CreateShareTokenAsync(RecipeShareTokenCreateModel model)
        {
            var currentPersonId = GetCurrentPersonId();
            var recipe = await _dbContext.Recipes
                .FirstOrDefaultAsync(r => r.Id == model.RecipeId);

            if (recipe == null)
            {
                throw new ArgumentException("Recipe not found");
            }

            // Only the author mints share tokens (previously any authenticated
            // user could mint a public token for any recipe id).
            if (recipe.AuthorId != currentPersonId)
            {
                throw new UnauthorizedAccessException("Only the recipe author can create share tokens.");
            }

            // Audience-scoped recipes are never shareable by token — scoped
            // visibility is the point (household-policies design doc §4).
            if (recipe.Visibility == Nom.Data.Recipe.RecipeVisibilityEnum.Audience)
            {
                throw new InvalidOperationException("Audience-scoped recipes cannot be shared by token.");
            }

            // Generate unique share token
            var shareToken = GenerateShareToken();

            var shareTokenEntity = new RecipeShareTokenEntity
            {
                RecipeId = model.RecipeId,
                Token = shareToken,
                Name = model.ShareName,
                IsPublic = model.IsPublic,
                UsesLeft = model.UsesLeft,
                ExpirationDate = model.ExpirationDate,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = currentPersonId
            };

            _dbContext.RecipeShareTokens.Add(shareTokenEntity);
            await _dbContext.SaveChangesAsync();

            return new RecipeShareTokenResponseModel
            {
                Id = shareTokenEntity.Id,
                RecipeId = shareTokenEntity.RecipeId,
                RecipeName = recipe.Name,
                ShareToken = shareTokenEntity.Token,
                ShareName = shareTokenEntity.Name ?? string.Empty,
                IsPublic = shareTokenEntity.IsPublic,
                UsesLeft = shareTokenEntity.UsesLeft,
                ExpirationDate = shareTokenEntity.ExpirationDate,
                CreatedDate = shareTokenEntity.CreatedDate,
                LastModifiedDate = shareTokenEntity.LastModifiedDate
            };
        }

        public async Task<List<RecipeShareTokenResponseModel>> GetRecipeShareTokensAsync(long recipeId)
        {
            var currentPersonId = GetCurrentPersonId();
            var isAuthor = await _dbContext.Recipes
                .AnyAsync(r => r.Id == recipeId && r.AuthorId == currentPersonId);
            if (!isAuthor)
            {
                return new List<RecipeShareTokenResponseModel>();
            }

            var shareTokens = await _dbContext.RecipeShareTokens
                .Include(st => st.Recipe)
                .Where(st => st.RecipeId == recipeId)
                .OrderByDescending(st => st.CreatedDate)
                .ToListAsync();

            return shareTokens.Select(st => new RecipeShareTokenResponseModel
            {
                Id = st.Id,
                RecipeId = st.RecipeId,
                RecipeName = st.Recipe?.Name ?? "Unknown",
                ShareToken = st.Token,
                ShareName = st.Name ?? string.Empty,
                IsPublic = st.IsPublic,
                CreatedDate = st.CreatedDate,
                LastModifiedDate = st.LastModifiedDate
            }).ToList();
        }

        public async Task<bool> DeleteShareTokenAsync(long shareTokenId)
        {
            var currentUserId = GetCurrentUserId();
            var shareToken = await _dbContext.RecipeShareTokens
                .FirstOrDefaultAsync(st => st.Id == shareTokenId && st.CreatedByPersonId == currentUserId);

            if (shareToken == null)
            {
                return false;
            }

            _dbContext.RecipeShareTokens.Remove(shareToken);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<RecipeShareTokenResponseModel?> GetRecipeByShareTokenAsync(string shareToken)
        {
            var shareTokenEntity = await _dbContext.RecipeShareTokens
                .Include(st => st.Recipe)
                .FirstOrDefaultAsync(st => st.Token == shareToken && st.IsPublic
                    && st.Recipe!.Visibility != Nom.Data.Recipe.RecipeVisibilityEnum.Audience);

            if (shareTokenEntity == null)
            {
                return null;
            }

            return new RecipeShareTokenResponseModel
            {
                Id = shareTokenEntity.Id,
                RecipeId = shareTokenEntity.RecipeId,
                RecipeName = shareTokenEntity.Recipe?.Name ?? "Unknown",
                ShareToken = shareTokenEntity.Token,
                ShareName = shareTokenEntity.Name ?? string.Empty,
                IsPublic = shareTokenEntity.IsPublic,
                CreatedDate = shareTokenEntity.CreatedDate,
                LastModifiedDate = shareTokenEntity.LastModifiedDate
            };
        }

        private string GenerateShareToken()
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString() + DateTime.UtcNow.Ticks);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-").Substring(0, 22);
        }

        // Timeline Events
        public async Task<RecipeTimelineEventResponseModel> CreateTimelineEventAsync(RecipeTimelineEventCreateModel model)
        {
            var currentUserId = GetCurrentUserId();
            var recipe = await _dbContext.Recipes
                .FirstOrDefaultAsync(r => r.Id == model.RecipeId);

            if (recipe == null)
            {
                throw new ArgumentException("Recipe not found");
            }

            var timelineEvent = new RecipeTimelineEventEntity
            {
                RecipeId = model.RecipeId,
                ActorId = currentUserId,
                EventTypeId = model.EventTypeId,
                Title = model.EventTitle,
                Description = model.EventDescription,
                EventDate = model.EventDate,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = currentUserId
            };

            _dbContext.RecipeTimelineEvents.Add(timelineEvent);
            await _dbContext.SaveChangesAsync();

            return new RecipeTimelineEventResponseModel
            {
                Id = timelineEvent.Id,
                RecipeId = timelineEvent.RecipeId,
                RecipeName = recipe.Name,
                EventTypeId = timelineEvent.EventTypeId,
                EventTypeName = timelineEvent.EventType?.Name ?? "Timeline Event",
                EventTitle = timelineEvent.Title,
                EventDescription = timelineEvent.Description,
                EventDate = timelineEvent.EventDate,
                CreatedDate = timelineEvent.CreatedDate,
                LastModifiedDate = timelineEvent.LastModifiedDate
            };
        }

        public async Task<List<RecipeTimelineEventResponseModel>> GetRecipeTimelineEventsAsync(long recipeId)
        {
            var events = await _dbContext.RecipeTimelineEvents
                .Include(te => te.Recipe)
                .Include(te => te.EventType)
                .Where(te => te.RecipeId == recipeId)
                .OrderByDescending(te => te.EventDate)
                .ToListAsync();

            return events.Select(te => new RecipeTimelineEventResponseModel
            {
                Id = te.Id,
                RecipeId = te.RecipeId,
                RecipeName = te.Recipe?.Name ?? "Unknown",
                EventTypeId = te.EventTypeId,
                EventTypeName = te.EventType?.Name ?? "Timeline Event",
                EventTitle = te.Title,
                EventDescription = te.Description,
                EventDate = te.EventDate,
                CreatedDate = te.CreatedDate,
                LastModifiedDate = te.LastModifiedDate
            }).ToList();
        }

        public async Task<bool> DeleteTimelineEventAsync(long eventId)
        {
            var currentUserId = GetCurrentUserId();
            var timelineEvent = await _dbContext.RecipeTimelineEvents
                .FirstOrDefaultAsync(te => te.Id == eventId && te.CreatedByPersonId == currentUserId);

            if (timelineEvent == null)
            {
                return false;
            }

            _dbContext.RecipeTimelineEvents.Remove(timelineEvent);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        // Notes
        public async Task<RecipeNoteResponseModel> CreateNoteAsync(RecipeNoteCreateModel model)
        {
            var currentUserId = GetCurrentUserId();
            var recipe = await _dbContext.Recipes
                .Include(r => r.Author)
                .FirstOrDefaultAsync(r => r.Id == model.RecipeId);

            if (recipe == null)
            {
                throw new ArgumentException("Recipe not found");
            }

            var note = new RecipeNoteEntity
            {
                RecipeId = model.RecipeId,
                AuthorId = currentUserId,
                Title = model.Title,
                Note = model.Note,
                IsPublic = model.IsPublic,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = currentUserId
            };

            _dbContext.RecipeNotes.Add(note);
            await _dbContext.SaveChangesAsync();

            return new RecipeNoteResponseModel
            {
                Id = note.Id,
                RecipeId = note.RecipeId,
                RecipeName = recipe.Name,
                AuthorId = note.AuthorId,
                AuthorName = recipe.Author?.Name ?? "Unknown",
                Title = note.Title,
                Note = note.Note,
                IsPublic = note.IsPublic,
                CreatedDate = note.CreatedDate,
                LastModifiedDate = note.LastModifiedDate
            };
        }

        public async Task<List<RecipeNoteResponseModel>> GetRecipeNotesAsync(long recipeId)
        {
            var currentUserId = GetCurrentUserId();
            var notes = await _dbContext.RecipeNotes
                .Include(n => n.Author)
                .Include(n => n.Recipe)
                .Where(n => n.RecipeId == recipeId && (n.IsPublic || n.AuthorId == currentUserId))
                .OrderByDescending(n => n.CreatedDate)
                .ToListAsync();

            return notes.Select(n => new RecipeNoteResponseModel
            {
                Id = n.Id,
                RecipeId = n.RecipeId,
                RecipeName = n.Recipe?.Name ?? "Unknown",
                AuthorId = n.AuthorId,
                AuthorName = n.Author?.Name ?? "Unknown",
                Title = n.Title,
                Note = n.Note,
                IsPublic = n.IsPublic,
                CreatedDate = n.CreatedDate,
                LastModifiedDate = n.LastModifiedDate
            }).ToList();
        }

        public async Task<bool> UpdateNoteAsync(long noteId, RecipeNoteCreateModel model)
        {
            var currentUserId = GetCurrentUserId();
            var note = await _dbContext.RecipeNotes
                .FirstOrDefaultAsync(n => n.Id == noteId && n.AuthorId == currentUserId);

            if (note == null)
            {
                return false;
            }

            note.Title = model.Title;
            note.Note = model.Note;
            note.IsPublic = model.IsPublic;
            note.LastModifiedDate = DateTime.UtcNow;
            note.LastModifiedByPersonId = currentUserId;

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteNoteAsync(long noteId)
        {
            var currentUserId = GetCurrentUserId();
            var note = await _dbContext.RecipeNotes
                .FirstOrDefaultAsync(n => n.Id == noteId && n.AuthorId == currentUserId);

            if (note == null)
            {
                return false;
            }

            _dbContext.RecipeNotes.Remove(note);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        // Recipe Actions
        public async Task<bool> MarkRecipeAsMadeAsync(long recipeId)
        {
            var currentUserId = GetCurrentUserId();
            var recipe = await _dbContext.Recipes.FindAsync(recipeId);

            if (recipe == null)
            {
                return false;
            }

            recipe.LastMade = DateTime.UtcNow;
            recipe.LastModifiedDate = DateTime.UtcNow;
            recipe.LastModifiedByPersonId = currentUserId;

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<DateTime?> GetRecipeLastMadeAsync(long recipeId)
        {
            var recipe = await _dbContext.Recipes.FindAsync(recipeId);
            return recipe?.LastMade;
        }
    }
}