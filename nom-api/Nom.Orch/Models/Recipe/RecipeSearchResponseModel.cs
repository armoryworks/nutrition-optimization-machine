// File: nom-api/Nom.Orch/Models/Recipe/RecipeSearchResponseModel.cs

using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeSearchResponseModel
    {
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public List<RecipeSearchResultModel> Results { get; set; } = new();
    }
} 