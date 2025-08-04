using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Export types for recipe bulk operations
    /// </summary>
    public enum ExportTypes
    {
        Json,
        Csv,
        Pdf
    }
} 