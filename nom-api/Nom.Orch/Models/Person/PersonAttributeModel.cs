namespace Nom.Orch.Models.Person
{
    public class PersonAttributeModel
    {
        public long Id { get; set; }
        public long PersonId { get; set; }
        public long AttributeTypeId { get; set; }
        public string AttributeTypeName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
