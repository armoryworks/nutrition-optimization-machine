using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Takes back applied USDA links from the audit log alone: every link apply journals the lines
    /// it moved, the food it merged away, the aliases it added and the status and values it gave,
    /// and this puts each of them back and returns the proposal to Pending.
    /// </summary>
    public interface ILinkApplyUndoService
    {
        /// <summary>
        /// Takes back, newest first, up to <paramref name="max"/> links applied at or after
        /// <paramref name="sinceUtc"/>, only those whose source starts with <paramref name="source"/>
        /// when it is given, and only those the lanes applied on their own when
        /// <paramref name="systemOnly"/> is set. A link with a line no longer where it put the line,
        /// or on a recipe whose rows a rebuild rewrote since, is skipped and reported, unless
        /// <paramref name="leaveMovedLines"/> is set: then that line stays where it is, counted, and
        /// its journal row is kept. A line an order-only pass moved is followed and taken back. An alias changed since the link
        /// is left as it is and counted; a USDA food stays curated while another applied link
        /// stands on it, and that link's undo restores the status later. A link taken back returns
        /// to Pending held for a person, so no lane applies it again on its own. Skipped links do
        /// not count towards <paramref name="max"/>. Without <paramref name="apply"/> nothing is
        /// written and the result is what the same call would do.
        /// </summary>
        Task<LinkApplyUndoResult> UndoAsync(DateTime sinceUtc, string? source = null, bool systemOnly = false, bool apply = false, int max = 500, int sampleSize = 50, bool leaveMovedLines = false);
    }
}
