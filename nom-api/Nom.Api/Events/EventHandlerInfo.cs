namespace Nom.Api.Events
{
    /// <summary>
    /// Information about an event handler
    /// </summary>
    public class EventHandlerInfo
    {
        public object Handler { get; set; } = null!;
        public int Priority { get; set; }
        public bool IsAsync { get; set; }
    }
}