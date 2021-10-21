using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.Logging.Compatibility;
using EPiServer.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Find.Cms
{
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class UsingSynonymService
    {
        private readonly SynonymLoader _synonymLoader;
        private readonly int MAX_SYNONYM_LOOKUPS = 50;
        private static ILog log = LogManager.GetLogger(typeof(SearchRequestExtensions));

        public UsingSynonymService(SynonymLoader synonymLoader)
        {
            _synonymLoader = synonymLoader;
        }

        public IQueriedSearch<TSource, QueryStringQuery> UsingSynonyms<TSource>(IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {
            if (!search.Client.Settings.Admin)
            {
                Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "Your index lacks an admin index. Please contact support.") { IsError = false });
                return new Search<TSource, QueryStringQuery>(search, context => { });
            }

            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                if (context.RequestBody.Query != null)
                {
                    BoolQuery newBoolQuery = new BoolQuery();
                    BoolQuery currentBoolQuery;
                    MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery;

                    if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentMinShouldMatchQueryStringQuery, out currentBoolQuery))
                    {
                        // Synonyms are only supported for QueryStringQuery
                        Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "The use of synonyms are only supported for QueryStringQueries, i.e. with the use of the .For() -extensions. The query will be executed without the use of synonyms.") { IsError = false });
                        return;
                    }

                    var query = QueryHelpers.GetRawQueryString(currentMinShouldMatchQueryStringQuery);
                    if (query.IsNullOrEmpty())
                    {
                        return;
                    }

                    var synonymDictionary = _synonymLoader.GetSynonyms(cacheDuration);

                    var queryPhrases = QueryHelpers.GetQueryPhrases(query).ToArray();
                    if (queryPhrases.Count() == 0)
                    {
                        return;
                    }

                    if (!GetQueryExpanded(queryPhrases, synonymDictionary, out string queryNonExpanded, out string queryExpanded, out List<string> queriesForMatch))
                    {
                        return;
                    }

                    // Add non expanded query. Using the custom MinimumShouldMatch if set.
                    if (queryNonExpanded.IsNotNullOrEmpty())
                    {
                        var minShouldMatchQuery = CreateMinShouldMatchQueryStringQuery(queryNonExpanded, true, "", currentMinShouldMatchQueryStringQuery);

                        // MinimumShouldMatch() overrides WithAndAsDefaultOperator()
                        if (currentMinShouldMatchQueryStringQuery.MinimumShouldMatch.IsNotNullOrEmpty())
                        {
                            minShouldMatchQuery.MinimumShouldMatch = currentMinShouldMatchQueryStringQuery.MinimumShouldMatch;
                        }
                        // Emulate WithAndAsDefaultOperator() using MinimumShouldMatch set to 100%
                        else if (currentMinShouldMatchQueryStringQuery.DefaultOperator == BooleanOperator.And)
                        {
                            minShouldMatchQuery.MinimumShouldMatch = "100%";
                        }

                        // We save all variations of queries with and without synonym expansions
                        // to be picked up by UsingImprovedRelevance()
                        // Only allow for 3 queriesForMatch
                        minShouldMatchQuery.ExpandedQuery = queriesForMatch.Take(3).ToArray();
                        newBoolQuery.Should.Add(minShouldMatchQuery);
                    }

                    // Add expanded query. MinimumShouldMatch is always 1 here. 
                    if (queryExpanded.IsNotNullOrEmpty())
                    {
                        var minShouldMatchQuery = CreateMinShouldMatchQueryStringQuery(queryExpanded, false, "1", currentMinShouldMatchQueryStringQuery);
                        newBoolQuery.Should.Add(minShouldMatchQuery);
                    }

                    if (newBoolQuery.IsNull())
                    {
                        return;
                    }

                    // Keep all QueryStringQuery except the first Should generated by For()
                    if (currentBoolQuery.IsNotNull())
                    {
                        foreach (var currentQuery in currentBoolQuery.Should.Skip(1))
                        {
                            newBoolQuery.Should.Add(currentQuery);
                        }

                        foreach (var currentQuery in currentBoolQuery.Must)
                        {
                            newBoolQuery.Must.Add(currentQuery);
                        }

                        foreach (var currentQuery in currentBoolQuery.MustNot)
                        {
                            newBoolQuery.MustNot.Add(currentQuery);
                        }
                    }

                    log.DebugFormat("Added QueryStringQuery {0} and {1} for synonyms.", queryNonExpanded, queryExpanded);
                    context.RequestBody.Query = newBoolQuery;

                }
            });
        }

        private static MinShouldMatchQueryStringQuery CreateMinShouldMatchQueryStringQuery(string query, bool autoGeneratePhraseQueries, string minShouldMatch, MultiFieldQueryStringQuery currentQueryStringQuery)
        {
            var minShouldMatchQuery = new MinShouldMatchQueryStringQuery(QueryHelpers.EscapeElasticSearchQuery(query));

            minShouldMatchQuery.RawQuery = currentQueryStringQuery.RawQuery;
            minShouldMatchQuery.AllowLeadingWildcard = currentQueryStringQuery.AllowLeadingWildcard;
            minShouldMatchQuery.AnalyzeWildcard = currentQueryStringQuery.AnalyzeWildcard;
            minShouldMatchQuery.Analyzer = currentQueryStringQuery.Analyzer;
            minShouldMatchQuery.AutoGeneratePhraseQueries = autoGeneratePhraseQueries;
            minShouldMatchQuery.Boost = currentQueryStringQuery.Boost;
            minShouldMatchQuery.EnablePositionIncrements = currentQueryStringQuery.EnablePositionIncrements;
            minShouldMatchQuery.FuzzyMinSim = currentQueryStringQuery.FuzzyMinSim;
            minShouldMatchQuery.FuzzyPrefixLength = currentQueryStringQuery.FuzzyPrefixLength;
            minShouldMatchQuery.LowercaseExpandedTerms = currentQueryStringQuery.LowercaseExpandedTerms;
            minShouldMatchQuery.PhraseSlop = currentQueryStringQuery.PhraseSlop;
            minShouldMatchQuery.DefaultField = currentQueryStringQuery.DefaultField;
            minShouldMatchQuery.Fields = currentQueryStringQuery.Fields;

            minShouldMatchQuery.MinimumShouldMatch = minShouldMatch.IsNotNullOrEmpty() ? minShouldMatch : "1";
            minShouldMatchQuery.DefaultOperator = BooleanOperator.Or;

            return minShouldMatchQuery;
        }

        // Get query non-expanded and expanded into queries to be used for for querystringquery and queries for match
        // Queries for querystringquery are a query without terms matching synonyms AND a query with only expanded synonyms
        // Queries for match are all expansion variations with the original query
        private bool GetQueryExpanded(string[] terms, Dictionary<String, HashSet<String>> synonymDictionary, out string queryNonExpanded, out string queryExpanded, out List<string> queriesForMatch)
        {
            queriesForMatch = new List<string>();
            queryExpanded = "";
            queryNonExpanded = string.Join(" ", terms);
            queriesForMatch.Add(queryNonExpanded);

            // Get out early if there are no synonyms
            if (synonymDictionary.Count == 0)
            {
                return true;
            }

            // Create a copy of terms to edit when find synonyms matches
            string[] nonExpandedTerms = (string[])terms.Clone();
            // List to keep expanded terms
            List<string> expandedTerms = new List<string>();

            // Iterate all terms and phrase variations, match synonym and expand them                    
            for (var s = 0; s <= terms.Count(); s++)
            {
                for (var c = 1; c <= terms.Count() - s; c++)
                {
                    var phrase = string.Join(" ", terms.Skip(s).Take(c));

                    if (MAX_SYNONYM_LOOKUPS >= s + c && synonymDictionary.TryGetValue(phrase.ToLowerInvariant(), out HashSet<string> matchingSynonyms))
                    {
                        // Terms/Phrase with synonym expansions i.e. (7 OR Seven)
                        expandedTerms.Add(ExpandPhrase(phrase, matchingSynonyms));

                        // Terms/Phrase variations with synonym expansions
                        foreach (var synonym in matchingSynonyms)
                        {
                            queriesForMatch.Add(string.Format("{0} {1} {2}", string.Join(" ", terms.Take(s)), synonym, string.Join(" ", terms.Skip(s + c))).Trim());
                        }

                        //Remove terms that were expanded
                        for (var x = s; s + c > x; x++)
                            nonExpandedTerms[x] = string.Empty;
                    }

                }

            }

            queryNonExpanded = string.Join(" ", nonExpandedTerms.Where(s => !string.IsNullOrEmpty(s)));
            queryExpanded = string.Join(" ", expandedTerms);

            return true;
        }

        // Get query expanded for matching synonyms
        // i.e. searching for 'dagis' where 'dagis' is a synonym for 'förskola' and 'lekis'
        // we will get the following expansion (dagis OR (förskola AND lekis))
        private static string ExpandPhrase(string phrase, HashSet<string> synonyms)
        {
            HashSet<string> expandedPhrases = new HashSet<string>();

            //Insert AND in between terms if not quoted
            if (!QueryHelpers.IsStringQuoted(phrase) && ContainsMultipleTerms(phrase))
            {
                phrase = string.Format("({0})", phrase.Replace(" ", string.Format(" {0} ", "AND")));
            }

            foreach (var synonym in synonyms)
            {
                //Insert AND in between terms if not quoted. Quoted not yet allowed by the Find UI though.
                if (!QueryHelpers.IsStringQuoted(synonym) && ContainsMultipleTerms(synonym))
                {
                    expandedPhrases.Add(string.Format("({0})", synonym.Replace(" ", string.Format(" {0} ", "AND"))));
                }
                else
                {
                    expandedPhrases.Add(synonym);
                }
            }

            return string.Format("({0}) ({1})", phrase, string.Join(" OR ", expandedPhrases));
        }

        private static bool ContainsMultipleTerms(string text)
        {
            return (text.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count() > 1);
        }

    }
}