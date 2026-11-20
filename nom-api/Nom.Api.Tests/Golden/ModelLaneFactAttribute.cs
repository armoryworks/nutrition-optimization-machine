using System.Linq;
using Nom.Import.Golden;
using Xunit;

namespace Nom.Api.Tests.Golden
{
    /// <summary>When a model-lane gate test is reported skipped instead of run.</summary>
    public enum ModelLaneSkip
    {
        WhenNoLaneIsChecked,
        WhenAnyLaneIsUnscored,
    }

    /// <summary>
    /// A model-lane gate test that is reported skipped, naming the lanes, while lanes with a
    /// scorer have no score record and are not yet required, so CI never shows the model-lane gate
    /// as passed for lanes it has not checked.
    /// </summary>
    public sealed class ModelLaneFactAttribute : FactAttribute
    {
        public ModelLaneFactAttribute(ModelLaneSkip when)
        {
            Skip = when == ModelLaneSkip.WhenNoLaneIsChecked ? ModelLaneGate.NothingChecked(GoldenSet.Folder) : ModelLaneGate.NotArmed(GoldenSet.Folder);
        }
    }

    /// <summary>Why the model-lane gate checks less than it should, or null when it checks every lane with a scorer.</summary>
    public static class ModelLaneGate
    {
        public static string? NothingChecked(string folder)
        {
            var scorers = ModelLanes.Floors(folder).Where(f => f.Value.Scorer).Select(f => f.Key).ToList();
            var unscored = ModelLanes.Unscored(folder);
            return scorers.All(unscored.Contains)
                ? $"no model lane is checked: {string.Join(", ", unscored)} have never been scored and model-lanes.json does not require them"
                : null;
        }

        public static string? NotArmed(string folder)
        {
            var unscored = ModelLanes.Unscored(folder);
            return unscored.Count > 0
                ? $"the model-lane gate is not armed for {string.Join(", ", unscored)}: never scored and not required; "
                  + "score each with --golden-score --record and set required: true in model-lanes.json in the same commit"
                : null;
        }
    }
}
