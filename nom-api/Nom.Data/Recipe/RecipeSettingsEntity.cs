// File: Nom.Data/Recipe/RecipeSettingsEntity.cs

using System;
using Nom.Data.Audit;

namespace Nom.Data.Recipe
{
    public class RecipeSettingsEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public string? SettingKey { get; set; }

        public string? SettingValue { get; set; }

        public string? SettingType { get; set; }
    }
}
