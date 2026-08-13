namespace Nom.Orch.Models.Plan
{
    /// <summary>
    /// The macro goals that actually apply to a person after resolution:
    /// their own goal when set, otherwise their household's, otherwise none.
    /// </summary>
    public class EffectiveMacroGoalModel : MacroGoalModel
    {
        /// <summary>Where the effective targets came from: "person", "household", or "none".</summary>
        public string Source { get; set; } = "none";
    }
}
