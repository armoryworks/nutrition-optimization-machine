namespace Nom.Orch.Models.Person
{
    public class UpdatePersonRequest
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? UserId { get; set; }
    }
} 