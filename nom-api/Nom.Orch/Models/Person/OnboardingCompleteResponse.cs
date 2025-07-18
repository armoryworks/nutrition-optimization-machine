// Nom.Orch/Models/Person/OnboardingCompleteResponse.cs
namespace Nom.Orch.Models.Person
{
    public class OnboardingCompleteResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long? NewPersonId { get; set; }
    }
}