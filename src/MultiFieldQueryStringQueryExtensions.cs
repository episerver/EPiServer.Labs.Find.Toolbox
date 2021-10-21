using Castle.Core.Internal;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.Logging.Compatibility;
using EPiServer.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EPiServer.Find.Cms
{

    public static class MultiFieldQueryStringQueryExtensions
    {
        private static Lazy<UsingSynonymService> _lazyUsingSynonymService = new Lazy<UsingSynonymService>(() => ServiceLocator.Current.GetInstance<UsingSynonymService>());
        private static ILog log = LogManager.GetLogger(typeof(SearchRequestExtensions));

        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {
            return UsingSynonymsImproved(search, _lazyUsingSynonymService.Value, cacheDuration);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, UsingSynonymService usingSynonymService, TimeSpan? cacheDuration = null)
        {
            return usingSynonymService.UsingSynonyms(search, cacheDuration);
        }

        public static IQueriedSearch<TSource, MinShouldMatchQueryStringQuery> MinimumShouldMatch<TSource>(this IQueriedSearch<TSource> search, string minMatch)
        {
            return new Search<TSource, MinShouldMatchQueryStringQuery>(search, context =>
            {
                var originalQuery = (QueryStringQuery)context.RequestBody.Query;
                var query = new MinShouldMatchQueryStringQuery(originalQuery.Query);
                query.RawQuery = originalQuery.RawQuery;
                query.AllowLeadingWildcard = originalQuery.AllowLeadingWildcard;
                query.AnalyzeWildcard = originalQuery.AnalyzeWildcard;
                query.Analyzer = originalQuery.Analyzer;
                query.AutoGeneratePhraseQueries = originalQuery.AutoGeneratePhraseQueries;
                query.Boost = originalQuery.Boost;
                query.DefaultOperator = originalQuery.DefaultOperator;
                query.EnablePositionIncrements = originalQuery.EnablePositionIncrements;
                query.FuzzyMinSim = originalQuery.FuzzyMinSim;
                query.FuzzyPrefixLength = originalQuery.FuzzyPrefixLength;
                query.LowercaseExpandedTerms = originalQuery.LowercaseExpandedTerms;
                query.PhraseSlop = originalQuery.PhraseSlop;
                query.DefaultField = originalQuery.DefaultField;
                var multiFieldQuery = context.RequestBody.Query as MultiFieldQueryStringQuery;
                if (multiFieldQuery != null)
                {
                    query.Fields = multiFieldQuery.Fields;
                }
                query.MinimumShouldMatch = minMatch;


                log.DebugFormat("Added MinimumShouldMatch to search");
                context.RequestBody.Query = query;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> UsingRelevanceImproved<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentMinShouldMatchQueryStringQuery, out currentBoolQuery))
                {
                    Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "The use of UsingRelevanceImproved() relies on a QueryStringQuery, i.e. with the use of the .For() -extensions.") { IsError = false });
                    return;
                }

                // Keep the current bool query with current queries if it exists
                if (currentBoolQuery.Should.Count > 0)
                {
                    newBoolQuery = currentBoolQuery;
                }
                else // Or use the QueryStringQuery
                {
                    newBoolQuery.Should.Add(currentMinShouldMatchQueryStringQuery);
                }

                // If .UsingImprovedSynonyms has not been used there is no ExpandedQuery for us then we use the query
                if (currentMinShouldMatchQueryStringQuery.ExpandedQuery.IsNull())
                {
                    currentMinShouldMatchQueryStringQuery.ExpandedQuery = new string[] { QueryHelpers.GetRawQueryString(currentMinShouldMatchQueryStringQuery) };
                }

                foreach (var query in currentMinShouldMatchQueryStringQuery.ExpandedQuery)
                {
                    var terms = QueryHelpers.GetQueryPhrases(query);

                    foreach (var fieldSelector in fieldSelectors)
                    {
                        var fieldNameLowercase = search.Client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
                        var fieldNameAnalyzed = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);

                        // Create PrefixQuery for single term queries larger than 2 characters
                        if (terms.Count() == 1 && terms.First().Length > 2)
                        {
                            var prefixQuery = new PrefixQuery(fieldNameLowercase, query.ToLower()) { Boost = 0.5 };
                            newBoolQuery.Should.Add(prefixQuery);
                        }

                        // Create PhraseQuery and PhrasePrefixQuery for multiple term queries                                              
                        if (terms.Count() > 1)
                        {
                            var phraseQuery = new PhraseQuery(fieldNameAnalyzed, query) { Boost = 10 };
                            var phrasePrefixQuery = new PhrasePrefixQuery(fieldNameLowercase, query.ToLower()) { Boost = 5 };
                            newBoolQuery.Should.Add(phraseQuery);
                            newBoolQuery.Should.Add(phrasePrefixQuery);
                        }

                    }

                }

                log.DebugFormat("Added PhraseQuery and PhrasePrefixQuery to search");
                context.RequestBody.Query = newBoolQuery;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentMinShouldMatchQueryStringQuery, out currentBoolQuery))
                {
                    Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "The use of FuzzyMatch relies on QueryStringQuery, i.e. with the use of the .For() -extensions.") { IsError = false });
                    return;
                }

                // Keep the current bool query with current queries if it exists
                if (currentBoolQuery.Should.Count > 0)
                {
                    newBoolQuery = currentBoolQuery;
                }
                else // Or use the QueryStringQuery
                {
                    newBoolQuery.Should.Add(currentMinShouldMatchQueryStringQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentMinShouldMatchQueryStringQuery);
                if (query.IsNullOrEmpty())
                {
                    return;
                }

                // Do not trigger on synonym expansions which are parenthesized and not on quoted searches.
                if (IsParenthesized(query) || QueryHelpers.IsStringQuoted(query))
                {
                    return;
                }

                var terms = QueryHelpers.GetQueryPhrases(query);

                // Only take terms > 2 chars and take max 3 of these
                string[] candidateTerms = terms.Where(x => x.Length > 2).Take(3).Select(x => string.Format("{0}{1}", x, "~")).ToArray();
                if (candidateTerms.Count() == 0)
                {
                    return;
                }

                List<string> fieldNames = new List<string>();
                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions
                                        .FieldNameConvention
                                        .GetFieldNameForAnalyzed(fieldSelector);
                    fieldNames.Add(fieldName);
                }

                var fuzzyQueryString = string.Join(" ", candidateTerms);
                var fuzzyQueryStringQuery = new MinShouldMatchQueryStringQuery(fuzzyQueryString)
                {
                    Fields = fieldNames,
                    DefaultOperator = currentMinShouldMatchQueryStringQuery.DefaultOperator,
                    MinimumShouldMatch = currentMinShouldMatchQueryStringQuery.MinimumShouldMatch,
                    FuzzyPrefixLength = 3,
                    Boost = 0.4
                };

                newBoolQuery.Should.Add(fuzzyQueryStringQuery);

                log.DebugFormat("Added fuzzyMatch {0} to search", fuzzyQueryString);

                context.RequestBody.Query = newBoolQuery;

            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentMinShouldMatchQueryStringQuery, out currentBoolQuery))
                {
                    // Synonyms are only supported for QueryStringQuery
                    Find.Tracing.Trace.Instance.Add(new TraceEvent(search, "The use of synonyms are only supported för QueryStringQueries, i.e. with the use of the .For() -extensions. The query will be executed without the use of synonyms.") { IsError = false });
                    return;
                }

                // Keep the current bool query with current queries if it exists
                if (currentBoolQuery.Should.Count > 0)
                {
                    newBoolQuery = currentBoolQuery;
                }
                else // Or use the QueryStringQuery
                {
                    newBoolQuery.Should.Add(currentMinShouldMatchQueryStringQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentMinShouldMatchQueryStringQuery);
                if (query.IsNullOrEmpty())
                {
                    return;
                }

                // Do not trigger on synonym expansions which are parenthesized and not on quoted searches.
                if (IsParenthesized(query) || QueryHelpers.IsStringQuoted(query))
                {
                    return;
                }

                var terms = QueryHelpers.GetQueryPhrases(query);

                // Only take terms > 2 chars and take max 3 of these
                string[] candidateTerms = terms.Where(x => x.Length > 2).Take(3).Select(x => string.Format("{0}{1}", x, "*")).ToArray();
                if (candidateTerms.Count() == 0)
                {
                    return;
                }

                List<string> fieldNames = new List<string>();
                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions
                                        .FieldNameConvention
                                        .GetFieldNameForAnalyzed(fieldSelector);
                    fieldNames.Add(fieldName);
                }

                var wildcardQueryString = string.Join(" ", candidateTerms);
                var wildcardQuery = new MinShouldMatchQueryStringQuery(wildcardQueryString)
                {
                    Fields = fieldNames,
                    DefaultOperator = currentMinShouldMatchQueryStringQuery.DefaultOperator,
                    MinimumShouldMatch = currentMinShouldMatchQueryStringQuery.MinimumShouldMatch,
                    Boost = 0.2
                };

                newBoolQuery.Should.Add(wildcardQuery);

                log.DebugFormat("Added wildcard {0} to search", wildcardQueryString);

                context.RequestBody.Query = newBoolQuery;
            });
        }

        private static bool IsParenthesized(string text)
        {
            return (text.StartsWith("(") && text.EndsWith(")"));
        }

    }
}