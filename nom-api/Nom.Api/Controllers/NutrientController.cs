using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Models.Nutrient;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NutrientController : BaseApiController
    {
        private readonly ApplicationDbContext _dbContext;

        public NutrientController(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Gets all nutrients for dropdowns and reference data.
        /// </summary>
        [HttpGet("all")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<NutrientModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllNutrients()
        {
            var nutrients = await _dbContext.Nutrients
                .Include(n => n.DefaultMeasurement)
                .AsNoTracking()
                .OrderBy(n => n.Name)
                .Select(n => new NutrientModel
                {
                    Id = n.Id,
                    Name = n.Name,
                    Description = n.Description,
                    DefaultMeasurementId = n.DefaultMeasurementId,
                    DefaultMeasurementName = n.DefaultMeasurement.Name,
                    DefaultMeasurementSymbol = n.DefaultMeasurement.Symbol,
                    Rank = n.Rank,
                    CreatedDate = n.CreatedDate,
                    LastModifiedDate = n.LastModifiedDate
                })
                .ToListAsync();

            return Ok(nutrients);
        }

        /// <summary>
        /// Gets a nutrient by its ID.
        /// </summary>
        [HttpGet("{id}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(NutrientModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetNutrientById(long id)
        {
            var nutrient = await _dbContext.Nutrients
                .Include(n => n.DefaultMeasurement)
                .AsNoTracking()
                .Where(n => n.Id == id)
                .Select(n => new NutrientModel
                {
                    Id = n.Id,
                    Name = n.Name,
                    Description = n.Description,
                    DefaultMeasurementId = n.DefaultMeasurementId,
                    DefaultMeasurementName = n.DefaultMeasurement.Name,
                    DefaultMeasurementSymbol = n.DefaultMeasurement.Symbol,
                    Rank = n.Rank,
                    CreatedDate = n.CreatedDate,
                    LastModifiedDate = n.LastModifiedDate
                })
                .FirstOrDefaultAsync();

            if (nutrient == null)
            {
                return NotFound(new { message = $"Nutrient with ID {id} not found." });
            }

            return Ok(nutrient);
        }
    }
}
