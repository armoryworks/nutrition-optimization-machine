using System.Collections.Generic;
using System.Text.Json;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// The scraper's source-check report (docs/scraper-integration.md, "Source check"): facts read
    /// from the site's robots.txt, reservation signals, licence and terms pages, sitemap and a
    /// sample of recipe pages, with the scraper's own verdict. Only the fields NOM reads are mapped;
    /// the report is stored whole.
    /// </summary>
    public class ScraperSourceCheckReport
    {
        public string Domain { get; set; } = string.Empty;
        public string? RuleVersion { get; set; }
        public string? Mode { get; set; }
        public string? Verdict { get; set; }
        public bool Complete { get; set; }
        public string? IncompleteReason { get; set; }
        public List<ScraperCheckReason> Reasons { get; set; } = new();
        public ScraperCheckAccess? Access { get; set; }
        public ScraperCheckRobots? Robots { get; set; }
        public ScraperCheckReservation? Reservation { get; set; }
        public ScraperCheckLicence? Licence { get; set; }
        public ScraperCheckTerms? Terms { get; set; }
        public ScraperCheckEnumeration? Enumeration { get; set; }
        public ScraperCheckSamples? Samples { get; set; }
    }

    public class ScraperCheckReason
    {
        public string Code { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }

    public class ScraperCheckRobots
    {
        public string? Url { get; set; }
        public string? Status { get; set; }
        public ScraperCheckRobotsGroup? NomBot { get; set; }
        public ScraperCheckRobotsGroup? General { get; set; }
        public List<ScraperCheckAiCrawler> AiCrawlers { get; set; } = new();
        public List<JsonElement> ContentSignals { get; set; } = new();
        public List<string> ProhibitionComments { get; set; } = new();
    }

    public class ScraperCheckAccess
    {
        public string? RefusedBeforeFetch { get; set; }
        public string? HomepageUrl { get; set; }
        public string? HomepageFinalUrl { get; set; }
        public string? HomepageOutcome { get; set; }
        public int? HomepageStatus { get; set; }
        public int? HomepageLinks { get; set; }
        public List<string> BotChallengePages { get; set; } = new();
        public List<string> RefusedPages { get; set; } = new();
    }

    public class ScraperCheckReservation
    {
        public string? TdmRepUrl { get; set; }
        public string? TdmRepStatus { get; set; }
        public List<JsonElement> TdmRepReserved { get; set; } = new();
        public List<string> TdmHeaderPages { get; set; } = new();
        public List<string> NoAiMetaPages { get; set; } = new();
        public List<string> NoAiHeaderPages { get; set; } = new();
    }

    public class ScraperCheckRobotsGroup
    {
        public string? Group { get; set; }
        public bool AllowsHomepage { get; set; }
        public bool DisallowsEverything { get; set; }
        public double? CrawlDelaySeconds { get; set; }
        public List<string> SamplePathsDisallowed { get; set; } = new();
    }

    public class ScraperCheckAiCrawler
    {
        public string Agent { get; set; } = string.Empty;
        public string Rule { get; set; } = string.Empty;
    }

    public class ScraperCheckLicence
    {
        public List<ScraperCheckStatement> Statements { get; set; } = new();
        public Dictionary<string, int> SampleLicences { get; set; } = new();
        public string? Basis { get; set; }
        public string? Kind { get; set; }
    }

    public class ScraperCheckStatement
    {
        public string Kind { get; set; } = string.Empty;
        public string? LicenceUrl { get; set; }
        public string? FoundAt { get; set; }
        public string? Where { get; set; }
        public string? Text { get; set; }
    }

    public class ScraperCheckTerms
    {
        public List<ScraperCheckTermsPage> Pages { get; set; } = new();
        public List<ScraperCheckProhibition> Prohibitions { get; set; } = new();
    }

    public class ScraperCheckTermsPage
    {
        public string Url { get; set; } = string.Empty;
        public int? HttpStatus { get; set; }
        public string? Outcome { get; set; }
    }

    public class ScraperCheckProhibition
    {
        public string? Category { get; set; }
        public string? Text { get; set; }
        public string? FoundAt { get; set; }
    }

    public class ScraperCheckEnumeration
    {
        public bool ViaSitemap { get; set; }
        public int RecipeUrlCount { get; set; }
        public bool StoppedByBudget { get; set; }
        public List<string> RefusedPages { get; set; } = new();
    }

    public class ScraperCheckSamples
    {
        public int Requested { get; set; }
        public int Attempted { get; set; }
        public int Fetched { get; set; }
        public int Refused { get; set; }
        public int WithRecipeJsonLd { get; set; }
        public int Complete { get; set; }
        public int English { get; set; }
        public int IngredientLines { get; set; }
        public int LinesWithQuantity { get; set; }
        public List<JsonElement> Credits { get; set; } = new();
        public List<ScraperCheckSamplePage> Pages { get; set; } = new();
    }

    public class ScraperCheckSamplePage
    {
        public string Url { get; set; } = string.Empty;
        public string? Outcome { get; set; }
        public int? HttpStatus { get; set; }
        public bool HasRecipeJsonLd { get; set; }
        public int Ingredients { get; set; }
        public int Steps { get; set; }
        public ScraperRecipe? Recipe { get; set; }
    }
}
