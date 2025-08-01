// File: Nom.Data/Recipe/RecipeSettingsEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Recipe
{
    [Table("RecipeSettings", Schema = "recipe")]
    public class RecipeSettingsEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [MaxLength(255)]
        public string? SettingKey { get; set; }

        [Column(TypeName = "text")]
        public string? SettingValue { get; set; }

        [MaxLength(255)]
        public string? SettingType { get; set; }
    }
}