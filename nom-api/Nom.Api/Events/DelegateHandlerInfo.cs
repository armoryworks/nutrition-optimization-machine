namespace Nom.Api.Events
{
    /// <summary>
    /// Information about a delegate event handler
    /// </summary>
    public class DelegateHandlerInfo
    {
        public Delegate Handler { get; set; } = null!;
        public int Priority { get; set; }
    }
}