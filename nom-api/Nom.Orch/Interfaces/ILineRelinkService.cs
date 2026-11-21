using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin repair of scraped recipe lines that sit on a curated food other than the one their
    /// own wording names ("1 cup water" on the well-water entry belongs on Water). Deterministic
    /// name rules only, every move is written to the audit log, and a move can be undone from it.
    /// </summary>
    public interface ILineRelinkService
    {
        Task<LineRelinkPreview> PreviewAsync(int sampleSize = 50);
        Task<LineRelinkResult> ApplyAsync(int maxLines = 5000);

        /// <summary>
        /// Moves every recipe line of one curated food to another, for a food the catalog does not
        /// need to tell apart from its main entry (well water and Water). A line that contains one
        /// of <paramref name="keepWords"/> stays ("delicious" keeps "red delicious apples" on its
        /// variety while "red apples" moves). Returns null when either food is not a live curated
        /// food or the target has no nutrition.
        /// </summary>
        Task<LineRelinkResult?> MoveFoodAsync(long fromIngredientId, long toIngredientId, string? keepWords = null);

        /// <summary>
        /// Moves the scraped lines of each given wording from the curated food they are on to the
        /// one named for them, for wordings a reviewer has judged ("tart apples" on a Granny Smith
        /// entry belongs on Apples). A line whose own text names the food it is on stays, whatever
        /// its wording normalises to ("zest and juice of 1 lemon" on Lemon Juice). Without
        /// <paramref name="apply"/> nothing is written, Remaining is the number of lines that
        /// would move, Planned lists them by food and wording and RejectedMoves the moves left out.
        /// </summary>
        Task<LineRelinkResult> MoveWordingsAsync(IReadOnlyCollection<LineRelinkWordingMove> moves, bool apply = false);

        /// <summary>
        /// Takes a duplicate food with no recipe lines left out of the catalog and leaves its name
        /// as an alias of the main entry alone, so a later line with that name lands there. With
        /// <paramref name="takeName"/> the main entry takes the retired food's name and its own
        /// former name becomes the alias. A main entry without a density or item weight takes
        /// over the retired food's. Nothing is retired while a recipe line or a household's
        /// data still points at the food. Returns null when either food is not a live curated food
        /// or the main entry has no nutrition.
        /// </summary>
        Task<FoodRetireResult?> RetireAsync(long fromIngredientId, long toIngredientId, bool takeName = false);

        /// <summary>
        /// Gives a curated food a new name, journaled so the undo sweep gives the old one back. A
        /// retired food that holds the name is renamed out of the way; a live food that holds it
        /// stops the rename, and the result says which food and how many lines it has. Aliases of
        /// the new name come off other foods. Returns null when the food is not a live curated
        /// food or the name is empty or unchanged.
        /// </summary>
        Task<FoodRenameResult?> RenameAsync(long ingredientId, string name, DateTime? at = null);

        /// <summary>
        /// Moves the scraped ingredient lines of every uncurated ingredient named with a verified
        /// wording to the food the wording table gives it, up to <paramref name="maxLines"/>, and
        /// retires each such ingredient left with no line and nothing else pointing at it,
        /// withdrawing its unreviewed link proposals. A pending unreviewed link proposal of any
        /// other ingredient whose wording the table judged, and whose pick is not the table's food,
        /// is withdrawn and journaled too. A wording whose food is gone, no longer
        /// curated or without nutrition moves nothing. Lines are re-positioned, never folded, and
        /// each move and retirement is journaled for the undo sweep. With <paramref name="batch"/>
        /// only that batch's wordings move lines and withdraw picks. Without
        /// <paramref name="apply"/> nothing is written and the result is what the same call would do.
        /// </summary>
        Task<VerifiedWordingLinkResult> LinkVerifiedWordingsAsync(bool apply = false, int maxLines = 20000, int sampleSize = 50, string? batch = null);

        /// <summary>
        /// Takes back what the verified wording link and the import did for each given wording, found
        /// by the journal rather than by time: the wording's sources (uncurated ingredients named with
        /// it, retired into its food or still live) get back the lines moved from them to its food and
        /// come back themselves with their withdrawn proposals; the picks withdrawn because the table
        /// judged the wording open again; and each line an import or a rebuild put on its food through
        /// the wording goes to the food the import finds without the wording. A line on a recipe
        /// rebuilt after its entry stays (undo the rebuild first). Without <paramref name="apply"/>
        /// nothing is written and the result counts the journal entries found.
        /// </summary>
        Task<WordingLinesUndoResult> UndoForSourcesAsync(IReadOnlyCollection<WordingUndoScope> wordings, bool apply = false);

        /// <summary>
        /// Puts back every line moved at or after <paramref name="sinceUtc"/> that still sits where
        /// the move left it, brings back every food retired since then, and joins again every row
        /// taken apart into one row per source line since then whose lines are as the unfold left
        /// them. Compound lines split or moved to their first alternative since then go back the
        /// same way. Newest first, so a line moved after it was unfolded goes back before it is joined;
        /// a proposal withdrawn with a retired food comes back after every other step, and only when
        /// that food is back. Without <paramref name="apply"/> the same work runs in one database transaction that is
        /// rolled back, so the result is what the same call would do and nothing is kept.
        /// </summary>
        Task<LineRelinkUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false);

        /// <summary>
        /// Takes back the given journal entries alone (line moves, retirements and renames, withdrawn
        /// proposals and picks), as <see cref="UndoAsync"/> takes back a time window: newest first, a
        /// line entry of a recipe rebuilt after it stays, and each entry taken back is removed.
        /// </summary>
        Task<LineRelinkUndoResult> UndoEntriesAsync(IReadOnlyCollection<long> entryIds);
    }
}
