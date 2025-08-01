namespace Nom.Data
{
    public abstract class BaseExpirationEntity : BaseEntity
    {
        public DateTime? ExpirationDate { get; set; }
    }
}