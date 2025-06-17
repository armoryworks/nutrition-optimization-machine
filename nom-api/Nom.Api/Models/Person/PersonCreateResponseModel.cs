// Nom.Api/Models/Person/PersonCreateResponseModel.cs
namespace Nom.Api.Models.Person
{
    /// <summary>
    /// Represents the data of a Person profile returned by the API after a creation operation.
    /// </summary>
    public class PersonCreateResponseModel // UPDATED CLASS NAME
    {
        public long Id { get; set; }
        public required string Name { get; set; }
        public string? UserId { get; set; } // The linked IdentityUser ID
    }
}