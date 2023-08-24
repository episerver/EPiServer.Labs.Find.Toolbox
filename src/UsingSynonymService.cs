using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Labs.Find.Toolbox
{
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class UsingSynonymService
    {
        private readonly SynonymLoader _synonymLoader;
        private readonly int MAX_SYNONYM_LOOKUPS = 50;
        private static ILogger log = LogManager.GetLogger(typeof(SearchRequestExtensions));

        public UsingSynonymService(SynonymLoader synonymLoader)
        {
            _synonymLoader = synonymLoader;
        }

        public IQueriedSearch<TSource, QueryStringQuery> UsingSynonyms<TSource>(IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {
            if (!search.Client.Settings.Admin)
            {
                Trace.Instance.Add(new TraceEvent(search, "Your index lacks an admin index. Please contact support.") { IsError = false });
                return new Search<TSource, QueryStringQuery>(search, context => { });
            }

            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                if (context.RequestBody.Query != null)
                {
                    BoolQuery newBoolQuery = new BoolQuery();

                    if (!QueryHelpers.GetFirstQueryStringQuery(context, out IQuery currentQuery, out BoolQuery currentBoolQuery))
                    {
                        // Synonyms are only supported for QueryStringQuery
                        Trace.Instance.Add(new TraceEvent(search, "The use of synonyms are only supported for QueryStringQueries, i.e. with the use of the .For() -extensions. The query will be executed without the use of synonyms.") { IsError = false });
                        return;
                    }

                    var query = QueryHelpers.GetRawQueryString(currentQuery);
                    if (query.IsNullOrEmpty())
                    {
                        return;
                    }

                    var synonymDictionary = _synonymLoader.GetSynonyms(cacheDuration);

                    var queryPhrases = QueryHelpers.GetQueryPhrases(query);
                    if (queryPhrases.Count() == 0)
                    {
                        return;
                    }                    

                    if (!GetQueryExpanded(queryPhrases, synonymDictionary, out string queryNonExpanded, out string queryExpanded, out List<string> queriesForMatch))
                    {
                        return;
                    }

                    // Add nonexpanded query. Using the custom MinimumShouldMatch if set.
                    if (queryNonExpanded.IsNotNullOrEmpty())
                    {
                        // MinimumShouldMatch() overrides WithAndAsDefaultOperator()
                        string minShouldMatch = string.Empty;
                        if (currentQuery is MinShouldMatchQueryStringQuery minShouldMatchQueryStringQuery)
                        {
                            if (minShouldMatchQueryStringQuery.MinimumShouldMatch.IsNotNullOrEmpty())
                            {
                                minShouldMatch = minShouldMatchQueryStringQuery.MinimumShouldMatch;
                            }
                            // Emulate WithAndAsDefaultOperator() using MinimumShouldMatch set to 100%
                            else if (minShouldMatchQueryStringQuery.DefaultOperator == BooleanOperator.And)
                            {
                                minShouldMatch = "100%";
                            }
                        }

                        var minShouldMatchQuery = CreateMinShouldMatchQueryStringQuery(queryNonExpanded.Quote(), minShouldMatch, true, currentQuery); ;

                        // We save all variations of queries with and without synonym expansions
                        // to be picked up by UsingImprovedRelevance()
                        // Only allow for 3 queriesForMatch
                        minShouldMatchQuery.QueriesForMatch = queriesForMatch.Take(3).ToArray();
                        newBoolQuery.Should.Add(minShouldMatchQuery);
                    }

                    // Add expanded query. MinimumShouldMatch is always 1 here. 
                    if (queryExpanded.IsNotNullOrEmpty())
                    {
                        var minShouldMatchQuery = CreateMinShouldMatchQueryStringQuery(queryExpanded, "1", false, currentQuery);
                        newBoolQuery.Should.Add(minShouldMatchQuery);
                    }

                    if (newBoolQuery.IsNull())
                    {
                        return;
                    }

                    // Keep all QueryStringQuery except the first Should generated by For()
                    if (currentBoolQuery.IsNotNull())
                    {
                        foreach (var tmpQuery in currentBoolQuery.Should.Skip(1))
                        {
                            newBoolQuery.Should.Add(tmpQuery);
                        }

                        foreach (var tmpQuery in currentBoolQuery.Must)
                        {
                            newBoolQuery.Must.Add(tmpQuery);
                        }

                        foreach (var tmpQuery in currentBoolQuery.MustNot)
                        {
                            newBoolQuery.MustNot.Add(tmpQuery);
                        }
                    }

                    log.Debug("Added QueryStringQuery {0}", queryExpanded);
                    context.RequestBody.Query = newBoolQuery;

                }
            });
        }

        private static MinShouldMatchQueryStringQuery CreateMinShouldMatchQueryStringQuery(string query, string minShouldMatch, bool autoGeneratePhraseQueries, IQuery currentQuery)
        {
            var minShouldMatchQuery = new MinShouldMatchQueryStringQuery(query);
            var queryStringQuery = (QueryStringQuery)currentQuery;

            minShouldMatchQuery.RawQuery = queryStringQuery.RawQuery;
            minShouldMatchQuery.AllowLeadingWildcard = queryStringQuery.AllowLeadingWildcard;
            minShouldMatchQuery.AnalyzeWildcard = queryStringQuery.AnalyzeWildcard;
            minShouldMatchQuery.Analyzer = queryStringQuery.Analyzer;
            minShouldMatchQuery.AutoGeneratePhraseQueries = autoGeneratePhraseQueries;
            minShouldMatchQuery.Boost = queryStringQuery.Boost;
            minShouldMatchQuery.EnablePositionIncrements = queryStringQuery.EnablePositionIncrements;
            minShouldMatchQuery.FuzzyMinSim = queryStringQuery.FuzzyMinSim;
            minShouldMatchQuery.FuzzyPrefixLength = queryStringQuery.FuzzyPrefixLength;
            minShouldMatchQuery.LowercaseExpandedTerms = queryStringQuery.LowercaseExpandedTerms;
            minShouldMatchQuery.PhraseSlop = queryStringQuery.PhraseSlop;
            minShouldMatchQuery.DefaultField = queryStringQuery.DefaultField;

            if (currentQuery is MultiFieldQueryStringQuery multiFieldQueryStringQuery)
            {
                minShouldMatchQuery.Fields = multiFieldQueryStringQuery.Fields;                
            }

            if (currentQuery is MinShouldMatchQueryStringQuery)
            {
                minShouldMatchQuery.MinimumShouldMatch = minShouldMatch;
            }

            minShouldMatchQuery.DefaultOperator = BooleanOperator.Or;

            return minShouldMatchQuery;
        }

        private bool GetQueryExpanded(string[] terms, Dictionary<String, HashSet<String>> synonymDictionary, out string queryNonExpanded, out string queryExpanded, out List<string> queriesForMatch)
        {
            queriesForMatch = new List<string>();
            queryExpanded = string.Empty;
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
                        // Expand synonym match i.e. 7 => Seven
                        expandedTerms.Add(ExpandPhrase(phrase, matchingSynonyms));

                        // Add synonym matches for MatchPhrasePrefix, MatchPhrase and Match
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

        private string ExpandPhrase(string phrase, HashSet<string> synonyms)
        {
            HashSet<string> expandedPhrases = new HashSet<string>();

            //Insert AND in between terms if not quoted
            if (!QueryHelpers.IsStringQuoted(phrase) && ContainsMultipleTerms(phrase))
            {
                phrase = string.Format("({0})", phrase.Quote().Replace(" ", " AND "));
            }

            foreach (var synonym in synonyms)
            {
                //Insert AND in between terms if not quoted. Quoted not yet allowed by the Find UI though.
                if (!QueryHelpers.IsStringQuoted(synonym) && ContainsMultipleTerms(synonym))
                {
                    expandedPhrases.Add(string.Format("({0})", synonym.Quote().Replace(" ", " AND ")));
                }
                else
                {
                    expandedPhrases.Add(synonym.Quote());
                }
            }

            return string.Format("({0} {1})", phrase, string.Join(" OR ", expandedPhrases));
        }

        private static bool ContainsMultipleTerms(string text)
        {
            return text.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count() > 1;
        }

    }
}