using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public enum FoodVerdict
    {
        Unsure,
        Food,
        NotFood,
    }

    /// <summary>
    /// The model behind the line classification and non-food screening lanes. It only classifies:
    /// what kind of line an unquantified ingredient line is, and whether a recipe is food at all.
    /// It never supplies amounts or rewrites text.
    /// </summary>
    public interface IRecipeClassificationModel
    {
        bool IsConfigured { get; }
        string ModelName { get; }

        /// <summary>
        /// For each line (by index), one of <see cref="Nom.Data.Recipe.RecipeIngredientLineKind.All"/>;
        /// lines the model gave no readable answer for are absent.
        /// </summary>
        Task<IReadOnlyDictionary<int, string>> ClassifyLinesAsync(string recipeName, IReadOnlyList<string> lines, CancellationToken cancellationToken = default);

        /// <summary>Whether the recipe is something people eat or drink; <see cref="FoodVerdict.Unsure"/> when the answer was unreadable.</summary>
        Task<FoodVerdict> IsFoodAsync(string recipeName, IReadOnlyList<string> ingredientLines, CancellationToken cancellationToken = default);
    }
}
