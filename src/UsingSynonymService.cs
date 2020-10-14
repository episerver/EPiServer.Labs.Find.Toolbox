using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EPiServer.Find.Cms
{
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class UsingSynonymService
    {
        private readonly SynonymLoader _synonymLoader;

        public UsingSynonymService(SynonymLoader synonymLoader)
        {
            _synonymLoader = synonymLoader;
        }

        public IQueriedSearch<TSource, QueryStringQuery> UsingSynonyms<TSource>(IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {

            if (search.Client.Settings.Admin)
            {
                return new Search<TSource, QueryStringQuery>(search, context =>
                {
                    if (context.RequestBody.Query != null)
                    {

                        BoolQuery newBoolQuery = new BoolQuery();
                        BoolQuery currentBoolQuery;
                        MultiFieldQueryStringQuery currentQueryStringQuery;

                        if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, out currentBoolQuery))
                        {
                            if (!QueryHelpers.TryGetQueryStringQuery(currentBoolQuery.Should[0], search, out currentQueryStringQuery))
                            {
                                return;
                            }
                        }
                        else
                        {
                            if (!QueryHelpers.TryGetQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
                            {
                                return;
                            }
                        }

                        var query = QueryHelpers.GetQueryString(currentQueryStringQuery);
                        if (query.IsNullOrEmpty())
                        {
                            return;
                        }

                        // If MinimumShouldMatch has been set previously pick up the minShouldMatch value
                        MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery;
                        string minShouldMatch = "";
                        if (QueryHelpers.TryGetMinShouldMatchQueryStringQuery(currentQueryStringQuery, out currentMinShouldMatchQueryStringQuery))
                        {
                            minShouldMatch = currentMinShouldMatchQueryStringQuery.MinimumShouldMatch;
                        }

                        var synonymDictionary = _synonymLoader.GetSynonyms(cacheDuration);

                        var queryPhrases = QueryHelpers.GetQueryPhrases(query).ToArray();
                        if (queryPhrases.Count() == 0)
                        {
                            return;
                        }

                        HashSet<string> queriesForMatch;
                        string queryNotExpanded = string.Join(" ", queryPhrases);
                        string queryExpanded;

                        if (!GetQueryExpanded(queryPhrases, synonymDictionary, out queryExpanded, out queriesForMatch))
                        {
                            return;
                        }

                        // Add non expanded query. Using the custom MinimumShouldMatch if set.
                        if (queryNotExpanded.IsNotNullOrEmpty())
                        {
                            var minShouldMatchQuery = CreateQuery(queryNotExpanded, currentQueryStringQuery, "");

                            // MinimumShouldMatch() overrides WithAndAsDefaultOperator()
                            if (minShouldMatch.IsNotNullOrEmpty())
                            {
                                minShouldMatchQuery.MinimumShouldMatch = minShouldMatch;
                            }
                            // Emulate WithAndAsDefaultOperator() using MinimumShouldMatch set to 100%
                            else if (currentQueryStringQuery.DefaultOperator == BooleanOperator.And)
                            {
                                minShouldMatchQuery.MinimumShouldMatch = "100%";
                            }

                            // We save all variations of queries with synonym expansions and without to be picked up by
                            // with match_phrase and match_phraze_prefix                            
                            minShouldMatchQuery.ExpandedQuery = queriesForMatch.ToArray();
                            newBoolQuery.Should.Add(minShouldMatchQuery);
                        }

                        // Add expanded query. MinimumShouldMatch is always 1 here. 
                        if (queryExpanded.IsNotNullOrEmpty())
                        {
                            var minShouldMatchQuery = CreateQuery(queryExpanded, currentQueryStringQuery, "1");
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

                        context.RequestBody.Query = newBoolQuery;

                    }
                });
            }
            else
            {
                Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "Your index does not support synonyms. Please contact support to have your account upgraded. Falling back to search without synonyms.") { IsError = false });
                return new Search<TSource, QueryStringQuery>(search, context => { });
            }
        }

        private static MinShouldMatchQueryStringQuery CreateQuery(string phrase, MultiFieldQueryStringQuery currentQueryStringQuery, string minShouldMatch)
        {
            string phrasesQuery = QueryHelpers.EscapeElasticSearchQuery(phrase);
            var minShouldMatchQuery = new MinShouldMatchQueryStringQuery(phrasesQuery);

            minShouldMatchQuery.RawQuery = currentQueryStringQuery.RawQuery;
            minShouldMatchQuery.AllowLeadingWildcard = currentQueryStringQuery.AllowLeadingWildcard;
            minShouldMatchQuery.AnalyzeWildcard = currentQueryStringQuery.AnalyzeWildcard;
            minShouldMatchQuery.Analyzer = currentQueryStringQuery.Analyzer;
            minShouldMatchQuery.AutoGeneratePhraseQueries = currentQueryStringQuery.AutoGeneratePhraseQueries;
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

        // Get query expanded into queries for querystringquery and queries for match
        // Queries for querystringquery are a query without terms matching synonyms AND a query with only expanded synonyms
        // Queries for match are all expansion variations with the original query
        private bool GetQueryExpanded(string[] terms, Dictionary<String, HashSet<String>> synonymDictionary, out string queryExpanded, out HashSet<string> queriesForMatch)
        {
            HashSet<string> expandedPhrases = new HashSet<string>();

            queriesForMatch = new HashSet<string>();
            queryExpanded = "";

            // Original query         
            queriesForMatch.Add(string.Join(" ", terms));

            // Bail out early if there are no synonyms
            if (synonymDictionary.Count == 0)
            {
                return true;
            }

            // Iterate all phrase variations, match synonyms, expand
            for (var s = 0; s <= terms.Count(); s++)
            {
                for (var c = 1; c <= terms.Count() - s; c++)
                {
                    var phrase = string.Join(" ", terms.Skip(s).Take(c));

                    HashSet<string> matchingSynonyms;
                    if (synonymDictionary.TryGetValue(phrase.ToLowerInvariant(), out matchingSynonyms))
                    {
                        foreach (var synonym in matchingSynonyms)
                        {
                            // Query variations with synonym expansions
                            queriesForMatch.Add(string.Format("{0} {1} {2}", string.Join(" ", terms.Take(s)), synonym, string.Join(" ", terms.Skip(s + c))).Trim());
                        }

                        // Query with only synonym expansions i.e. (7 OR Seven)
                        expandedPhrases.Add(ExpandPhrase(phrase, matchingSynonyms));
                    }
                }
            }

            queryExpanded = string.Join(" ", expandedPhrases);

            return true;
        }

        // Return phrase expanded with matching synonym
        // Searching for 'dagis' where 'dagis' is a synonym for 'förskola' and 'lekis'
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
                if (!QueryHelpers.IsStringQuoted(synonym) && ContainsMultipleTerms(phrase))
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