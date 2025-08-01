using System.ComponentModel.DataAnnotations;

namespace Nom.Data
{
    public abstract class BaseExpirationLimitedUseEntity : BaseExpirationEntity
    {
        public int? UsesLeft { get; set; }
    }
}