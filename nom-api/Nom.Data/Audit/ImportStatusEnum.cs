// Nom.Data/Audit/ImportStatusEnum.cs
namespace Nom.Data.Audit
{
    /// <summary>
    /// Represents the possible statuses of a recipe import job.
    /// </summary>
    public enum ImportStatusEnum
    {
        /// <summary>
        /// The default, initial state of an import job.
        /// </summary>
        Queued = 0,

        /// <summary>
        /// The import job is currently running and processing data.
        /// </summary>
        Running = 1,

        /// <summary>
        /// The import job has completed successfully.
        /// </summary>
        Completed = 2,

        /// <summary>
        /// The import job encountered an error and failed to complete.
        /// </summary>
        Failed = 3,

        /// <summary>
        /// The import job was explicitly canceled by a user or system.
        /// </summary>
        Canceled = 4
    }
}
