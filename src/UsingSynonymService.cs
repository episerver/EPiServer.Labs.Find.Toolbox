using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
                        MultiFieldQueryStringQuery currentQueryStringQuery;
                        if (!TryGetQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
                        {
                            return;
                        }

                        var query = GetQueryString(currentQueryStringQuery);
                        if (query.IsNullOrEmpty())
                        {
                            return;
                        }

                        // If MinimumShouldMatch has been set previously pick up the minShouldMatch value
                        MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery;
                        string minShouldMatch = "";
                        if (TryGetMinShouldMatchQueryStringQuery(context.RequestBody.Query, search, out currentMinShouldMatchQueryStringQuery))
                        {
                            minShouldMatch = currentMinShouldMatchQueryStringQuery.MinimumShouldMatch;
                        }

                        var synonymDictionary = _synonymLoader.GetSynonyms(cacheDuration);

                        var queryPhrases = GetQueryPhrases(query).Take(50).ToArray();                               // Collect all phrases in user query. Max 50 phrases.
                        if (queryPhrases.Count() == 0)
                        {
                            return;
                        }

                        var phraseVariations = GetPhraseVariations(queryPhrases);                                   // 'Alloy tech now' would result in Alloy, 'Alloy tech', 'Alloy tech now', tech, 'tech now' and now
                        var phrasesToExpand = GetPhrasesToExpand(phraseVariations, synonymDictionary);              // Return all phrases with expanded synonyms                        
                        var nonExpandedPhrases = GetPhrasesNotToExpand(queryPhrases, phrasesToExpand);              // Return all phrases that didn't get expanded
                        var expandedPhrases = ExpandPhrases(phrasesToExpand, synonymDictionary);                    // Expand phrases                                
                        var allPhrases = new HashSet<string>(nonExpandedPhrases.Union(expandedPhrases));            // Merge nonExpandedPhrases and expandedPhrases


                        // Add query for all phrases
                        if (allPhrases.Count() > 0)
                        {
                            // If there are only synonym expansions be less strict on required matches                                        
                            string minShouldMatchFinal = nonExpandedPhrases.Count() == 0 && expandedPhrases.Count() > 0 ? "1<40%" : minShouldMatch;
                            var minShouldMatchQueryStringQuery = CreateQuery(allPhrases, currentQueryStringQuery, minShouldMatch.IsNotNullOrEmpty() ? minShouldMatchFinal : "");
                            context.RequestBody.Query = minShouldMatchQueryStringQuery;
                        }

                    }
                });
            }
            else
            {
                Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "Your index does not support synonyms. Please contact support to have your account upgraded. Falling back to search without synonyms.") { IsError = false });
                return new Search<TSource, QueryStringQuery>(search, context => { });
            }
        }

        private bool TryGetMinShouldMatchQueryStringQuery<TSource>(IQuery query, IQueriedSearch<TSource> search, out MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery)
        {
            currentMinShouldMatchQueryStringQuery = query as MinShouldMatchQueryStringQuery;
            if (currentMinShouldMatchQueryStringQuery == null)
            {
                return false;
            }

            return true;
        }

        private bool TryGetQueryStringQuery<TSource>(IQuery query, IQueriedSearch<TSource> search, out MultiFieldQueryStringQuery currentQueryStringQuery)
        {
            currentQueryStringQuery = query as MultiFieldQueryStringQuery;
            if (currentQueryStringQuery == null)
            {
                // synonyms are only supported for QueryStringQuery
                Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "The use of synonyms are only supported för QueryStringQueries, i.e. with the use of the .For() -extensions. The query will be executed without the use of synonyms.") { IsError = false });
                return false;
            }

            return true;
        }

        /// <summary>
        /// Get query, default operator, analyzer and fields from current queryStringQuery (produced by For())
        /// </summary>
        /// <param name="currentQueryStringQuery"></param>
        /// <returns></returns>
        private string GetQueryString(MultiFieldQueryStringQuery currentQueryStringQuery)
        {
            return (currentQueryStringQuery.Query ?? string.Empty).ToString();
        }

        private static MinShouldMatchQueryStringQuery CreateQuery(HashSet<string> phrases, MultiFieldQueryStringQuery currentQueryStringQuery, string minShouldMatch)
        {
            string phrasesQuery = EscapeElasticSearchQuery(string.Join(" ", phrases.ToArray()));
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

            // MinimumShouldMatch() overrides WithAndAsDefaultOperator()
            if (minShouldMatch.IsNotNullOrEmpty())
            {
                minShouldMatchQuery.MinimumShouldMatch = minShouldMatch;
                minShouldMatchQuery.DefaultOperator = BooleanOperator.Or;
            }
            else
            {
                minShouldMatchQuery.DefaultOperator = currentQueryStringQuery.DefaultOperator;
            }

            return minShouldMatchQuery;
        }

        // Return all terms and phrases in query
        private static string[] GetQueryPhrases(string query)
        {
            // Replace double space, tabs with single whitespace and trim space on side
            string cleanedQuery = Regex.Replace(UnescapeElasticSearchQuery(query), @"\s+", " ").Trim();

            // Clean diactrics
            cleanedQuery = CleanDiactrics(cleanedQuery);

            // Match single terms and quoted terms, allow hyphens and ´'` in terms, allow space between quotes and word.
            return Regex.Matches(cleanedQuery, @"([\w-]+)|([""][\s\w-´'`]+[""])").Cast<Match>().Select(c => c.Value.Trim()).ToArray();
        }

        // Return all combinations of phrases in order
        private static HashSet<string> GetPhraseVariations(string[] terms)
        {
            HashSet<string> candidates = new HashSet<string>();

            for (var s = 0; s <= terms.Count(); s++)
            {
                for (var c = 1; c <= terms.Count() - s; c++)
                {
                    var term = string.Join(" ", terms.Skip(s).Take(c));
                    candidates.Add(term);
                }
            }

            return candidates;
        }

        // Get phrase (not variations) that should not get expanded
        private static HashSet<string> GetPhrasesNotToExpand(string[] terms, HashSet<string> phrasesToExpand)
        {
            string[] phrasesNotToExpand = terms.Except(phrasesToExpand).ToArray();

            // Exclude phrases that didn't get expanded but share terms with phrasesToExpand
            foreach (var phraseToExpand in phrasesToExpand)
            {
                phrasesNotToExpand = phrasesNotToExpand.Except(phraseToExpand.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            }

            return new HashSet<string>(phrasesNotToExpand);
        }

        // Get phrase variations that should get expanded (that match synonyms)
        private static HashSet<string> GetPhrasesToExpand(HashSet<string> termVariations, Dictionary<String, HashSet<String>> synonymDictionary)
        {
            return new HashSet<string>(termVariations.Intersect(synonymDictionary.Keys));
        }

        // Return phrases with their expanded synonym
        private static HashSet<string> ExpandPhrases(HashSet<string> phrasesToExpand, Dictionary<String, HashSet<String>> synonymDictionary)
        {
            HashSet<string> queryList = new HashSet<string>();

            foreach (var match in phrasesToExpand)
            {
                string expPhrase = ExpandPhrase(match, synonymDictionary[match]);
                queryList.Add(expPhrase);
            }

            return queryList;
        }

        // Return phrase expanded with matching synonym
        // Searching for 'dagis' where 'dagis' is a synonym for 'förskola' and 'lekis'
        // we will get the following expansion (dagis OR (förskola AND lekis))
        private static string ExpandPhrase(string phrase, HashSet<string> synonyms)
        {
            HashSet<string> expandedPhrases = new HashSet<string>();

            //Insert AND in between terms if not quoted
            if (!IsStringQuoted(phrase))
            {
                phrase = phrase.Replace(" ", string.Format(" {0} ", "AND"));
            }

            foreach (var synonym in synonyms)
            {
                //Insert AND in between terms if not quoted. Quoted not yet allowed by the Find UI though.
                if (!IsStringQuoted(synonym))
                {
                    expandedPhrases.Add(string.Format("(({0}) OR ({1}))", phrase, synonym.Replace(" ", string.Format(" {0} ", "AND"))));
                }
                else
                {
                    expandedPhrases.Add(string.Format("(({0}) OR ({1}))", phrase, synonym));
                }

            }

            return string.Join(" ", expandedPhrases);
        }

        private static bool IsStringQuoted(string text)
        {
            return (text.StartsWith("\"") && text.EndsWith("\""));
        }

        private static bool ContainsMultipleTerms(string text)
        {
            return (text.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count() > 1);
        }

        private static string UnescapeElasticSearchQuery(string s)
        {
            return s.Replace("\\", "");
        }

        private static string EscapeElasticSearchQuery(string s)
        {
            return s.Replace("-", "\\-");
        }

        private static string CleanDiactrics(string text)
        {
            string decomposed = text.Normalize(NormalizationForm.FormD);
            char[] filtered = decomposed
                .Where(c => char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray();

            return new String(filtered);
        }
    }
}