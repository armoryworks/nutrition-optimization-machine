namespace Nom.Orch.Models.Label
{
    public class LabelResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public string? GroupName { get; set; }
    }
}
