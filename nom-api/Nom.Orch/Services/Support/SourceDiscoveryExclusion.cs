using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The discovery job's <c>excludeDomains</c>: every host NOM holds that the job may not fetch, with why, and the list as
    /// sent. A list entry excludes the host itself, its <c>www.</c> form and every subdomain (the scraper's rule).
    /// </summary>
    public sealed record SourceDiscoveryExclusionList(
        IReadOnlyDictionary<string, string> Held,
        IReadOnlyList<string> Entries,
        int Barred,
        IReadOnlyList<string> LeftOut,
        IReadOnlyList<string> NotHostNames,
        SourceDiscoveryExclusionCursor? Cursor = null)
    {
        /// <summary>Whether the forbidden and permission-needed hosts alone pass the scraper's limit, so no list can hold them all.</summary>
        public bool TooManyBarred => Barred > SourceDiscoveryExclusion.MaxEntries;

        /// <summary>Unclassed or rejected candidate hosts not sent because an approved source that may be fetched has the host or a subdomain of it.</summary>
        public IReadOnlyList<string> Yielded { get; init; } = Array.Empty<string>();

        private HashSet<string>? _heldHosts;

        private HashSet<string>? _sentHosts;

        /// <summary>Whether a held host (sent or not) covers <paramref name="domain"/>: the host itself or a parent of it.</summary>
        public bool Covers(string? domain) =>
            SourceDiscoveryExclusion.Covers(_heldHosts ??= Held.Keys.ToHashSet(StringComparer.Ordinal), domain);

        /// <summary>Whether an entry as sent covers <paramref name="domain"/>.</summary>
        public bool Sent(string? domain) =>
            SourceDiscoveryExclusion.Covers(_sentHosts ??= Entries.ToHashSet(StringComparer.Ordinal), domain);
    }

    /// <summary>Where the turn of the rejected and unclassed hosts stopped: the last one sent, by when NOM first held its row and its name.</summary>
    public sealed record SourceDiscoveryExclusionCursor(DateTime Since, string Host);

    /// <summary>
    /// Builds the exclusion list a discovery job is sent (scraper contract 2.5): every host of a source or candidate that NOM
    /// holds as <b>forbidden</b> or <b>permission-needed</b> (a source by its effective rights class, a candidate by the class
    /// its last check's report gives it), <b>rejected</b> (a source or candidate an admin rejected) or <b>unclassed</b> (no
    /// class yet: a candidate whose last check gave none, a lead never checked, and a source with no class that may not be
    /// fetched). A source that may be fetched is never held; which may be fetched follows
    /// <see cref="Settings.SourceRightsSettings.Enforce"/> as seeding does (<see cref="MayFetch"/>), and every other source
    /// is held by its class with the gate on or off. A candidate's reasons always hold, as the check lane's own bars do. The rows of one host (<c>budgetbytes.com</c>, <c>www.budgetbytes.com</c>) are one
    /// entry, under the strongest reason. At most <see cref="MaxEntries"/> entries go: every forbidden and permission-needed
    /// host first (when they alone pass the limit no list is built and discovery does not run), then the rejected and
    /// unclassed hosts in turn: they stand in the order NOM first held their rows, and each run goes on from the host after
    /// the last one the run before it sent, round the list, so a newly held host joins at the end and every host is sent
    /// within a bounded number of runs, however many new hosts come in. When the forbidden and permission-needed hosts fill
    /// all <see cref="MaxEntries"/> entries there is no room, and the others are left out (shown) until there is. Pure, no I/O.
    /// </summary>
    public static class SourceDiscoveryExclusion
    {
        public const int MaxEntries = 5000;
        public const string Forbidden = SourceRightsRule.Forbidden;
        public const string PermissionNeeded = SourceRightsRule.PermissionNeeded;
        public const string Rejected = "rejected";
        public const string Unclassed = "unclassed";

        private const int MaxHostLength = 253;

        /// <summary>The reasons in the order they are sent and shown, strongest first.</summary>
        public static readonly IReadOnlyList<string> Reasons = new[] { Forbidden, PermissionNeeded, Rejected, Unclassed };

        private static readonly IdnMapping Idn = new();

        /// <summary>Why a source is held, or null when it is not.</summary>
        public static string? ForSource(ScrapingSourceEntity source, bool enforce)
        {
            var reasons = new List<string>();
            if (source.Status == ScrapingSourceStatusEnum.Rejected) reasons.Add(Rejected);
            if (!MayFetch(source, enforce))
            {
                var effective = SourceRightsService.EffectiveClass(source);
                if (effective is Forbidden or PermissionNeeded) reasons.Add(effective);
                else if (effective == null) reasons.Add(Unclassed);
            }

            return Strongest(reasons);
        }

        /// <summary>
        /// Whether the source may be fetched: under the rights gate only one that <see cref="SourceRightsService.MayFetch"/>
        /// allows; without it every approved web source, as every one seeds and is harvested then.
        /// </summary>
        public static bool MayFetch(ScrapingSourceEntity source, bool enforce) =>
            enforce
                ? SourceRightsService.MayFetch(source)
                : source.Status == ScrapingSourceStatusEnum.Approved && source.Kind == ScrapingSourceEntity.WebKind;

        /// <summary>
        /// Whether a candidate's hold yields to an approved source that may be fetched: the candidate is unclassed or rejected
        /// (no facts, or an admin's earlier no) and its host is the source's host or a parent of it, so the source's own
        /// check and the admin's later approval decide. A candidate whose report bars it never yields.
        /// </summary>
        public static bool YieldsTo(string candidateDomain, string reason, IEnumerable<string> fetchableHosts)
        {
            if (reason is not (Rejected or Unclassed)) return false;
            var host = Entry(candidateDomain);
            if (host == null) return false;
            var one = new HashSet<string>(StringComparer.Ordinal) { host };
            return fetchableHosts.Any(seed => Covers(one, seed));
        }

        /// <summary>Why a candidate is held, given the class its last check's report gives it (null for none), or null when it is not.</summary>
        public static string? ForCandidate(SourceCandidateEntity candidate, string? rightsClass)
        {
            if (candidate.State == SourceCandidateEntity.Approved) return null;
            var reasons = new List<string>();
            if (candidate.State == SourceCandidateEntity.Rejected) reasons.Add(Rejected);
            if (rightsClass is Forbidden or PermissionNeeded) reasons.Add(rightsClass);
            else if (rightsClass == null) reasons.Add(Unclassed);
            return Strongest(reasons);
        }

        /// <summary>
        /// The list for one run: <paramref name="held"/> (domain, reason, when NOM first held the row) collapsed to one entry
        /// per host under its strongest reason and its earliest time, the forbidden and permission-needed hosts always, then
        /// the others in the order of that time and name, from the host after <paramref name="cursor"/>, round the list.
        /// </summary>
        public static SourceDiscoveryExclusionList Build(IEnumerable<(string Domain, string Reason, DateTime Since)> held, SourceDiscoveryExclusionCursor? cursor)
        {
            var hosts = new Dictionary<string, string>(StringComparer.Ordinal);
            var since = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            var notHostNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (domain, reason, at) in held)
            {
                var host = Entry(domain);
                if (host == null)
                {
                    if (!string.IsNullOrWhiteSpace(domain)) notHostNames.Add(domain.Trim());
                    continue;
                }

                hosts[host] = hosts.TryGetValue(host, out var had) ? Strongest(new[] { had, reason })! : reason;
                since[host] = since.TryGetValue(host, out var first) && first <= at ? first : at;
            }

            var barredHosts = hosts.Where(h => h.Value is Forbidden or PermissionNeeded).Select(h => h.Key).ToHashSet(StringComparer.Ordinal);
            var barred = Ordered(hosts.Where(h => barredHosts.Contains(h.Key) && !CoveredByParent(h.Key, barredHosts)));
            var rest = hosts.Where(h => !barredHosts.Contains(h.Key) && !Covers(barredHosts, h.Key))
                .Select(h => h.Key)
                .OrderBy(h => since[h]).ThenBy(h => h, StringComparer.Ordinal)
                .ToList();
            if (barred.Count > MaxEntries)
                return new SourceDiscoveryExclusionList(hosts, Array.Empty<string>(), barred.Count, rest, notHostNames.ToList(), cursor);

            var room = MaxEntries - barred.Count;
            if (rest.Count <= room)
                return new SourceDiscoveryExclusionList(hosts, barred.Concat(rest.OrderBy(h => Rank(hosts[h])).ThenBy(h => h, StringComparer.Ordinal)).ToList(), barred.Count, Array.Empty<string>(), notHostNames.ToList(), cursor);
            if (room == 0)
                return new SourceDiscoveryExclusionList(hosts, barred, barred.Count, rest, notHostNames.ToList(), cursor);

            var start = cursor == null ? 0 : rest.FindIndex(h => since[h] > cursor.Since || (since[h] == cursor.Since && string.CompareOrdinal(h, cursor.Host) > 0));
            if (start < 0) start = 0;
            var taken = Enumerable.Range(0, room).Select(i => rest[(start + i) % rest.Count]).ToList();
            var takenSet = taken.ToHashSet(StringComparer.Ordinal);
            var last = taken[^1];
            return new SourceDiscoveryExclusionList(hosts, barred.Concat(taken).ToList(), barred.Count, rest.Where(h => !takenSet.Contains(h)).ToList(), notHostNames.ToList(),
                new SourceDiscoveryExclusionCursor(since[last], last));
        }

        /// <summary>
        /// A domain as the scraper reads an <c>excludeDomains</c> entry: lower case, no scheme, path, port, leading
        /// <c>www.</c> or <c>*.</c> or trailing dot, international names in their ASCII (punycode) form; null when it is not a
        /// host name the scraper takes (no dot, an address literal, a label with characters other than letters, digits,
        /// <c>-</c> and <c>_</c>, longer than 253 characters).
        /// </summary>
        public static string? Entry(string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return null;
            var text = domain.Trim().ToLowerInvariant();
            if (text.StartsWith("*.", StringComparison.Ordinal)) text = text[2..];
            if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.Basic)) return null;
            var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
            try
            {
                host = Idn.GetAscii(host).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
            if (host.Length == 0 || host.Length > MaxHostLength || !host.Contains('.', StringComparison.Ordinal)) return null;
            return host.Split('.').All(label => label.Length > 0 && label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')) ? host : null;
        }

        /// <summary>Whether one of <paramref name="entries"/> is <paramref name="domain"/>'s host or a parent of it.</summary>
        public static bool Covers(IEnumerable<string> entries, string? domain)
        {
            var host = Entry(domain);
            if (host == null) return false;
            var set = entries as ISet<string> ?? entries.ToHashSet(StringComparer.Ordinal);
            if (set.Contains(host)) return true;
            return CoveredByParent(host, set);
        }

        private static bool CoveredByParent(string host, ICollection<string> entries)
        {
            for (var dot = host.IndexOf('.', StringComparison.Ordinal); dot >= 0 && dot < host.Length - 1; dot = host.IndexOf('.', dot + 1))
            {
                if (entries.Contains(host[(dot + 1)..])) return true;
            }

            return false;
        }

        private static List<string> Ordered(IEnumerable<KeyValuePair<string, string>> hosts) =>
            hosts.OrderBy(h => Rank(h.Value)).ThenBy(h => h.Key, StringComparer.Ordinal).Select(h => h.Key).ToList();

        private static string? Strongest(IEnumerable<string> reasons) =>
            reasons.OrderBy(Rank).FirstOrDefault();

        private static int Rank(string reason)
        {
            for (var i = 0; i < Reasons.Count; i++)
            {
                if (Reasons[i] == reason) return i;
            }

            return Reasons.Count;
        }
    }
}
