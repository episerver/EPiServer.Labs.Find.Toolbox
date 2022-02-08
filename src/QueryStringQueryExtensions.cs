using Castle.Core.Internal;
using EPiServer.Find.Api.Querying;
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
    public static class QueryStringQueryExtensions
    {
        private static Lazy<UsingSynonymService> _lazyUsingSynonymService = new Lazy<UsingSynonymService>(() => ServiceLocator.Current.GetInstance<UsingSynonymService>());
        private static ILog log = LogManager.GetLogger(typeof(SearchRequestExtensions));

        private static double PREFIXQUERY_DEFAULT_BOOST = 0.5;
        private static double PHRASEPREFIXQUERY_DEFAULT_BOOST = 5;
        private static double PHRASEQUERY_DEFAULT_BOOST = 10;
        private static double WILDCARD_DEFAULT_BOOST = 0.2;
        private static double FUZZY_DEFAULT_BOOST = 0.4;

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
                if (context.RequestBody.Query is MultiFieldQueryStringQuery multiFieldQuery)
                {
                    query.Fields = multiFieldQuery.Fields;
                }
                query.MinimumShouldMatch = minMatch;
                
                context.RequestBody.Query = query;
                log.DebugFormat("Added MinimumShouldMatch to search");
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> UsingRelevanceImproved<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                IQuery currentQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentQuery, out currentBoolQuery))
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
                    newBoolQuery.Should.Add(currentQuery);
                }


                IEnumerable<string> expandedQuery = null;

                // If .UsingImprovedSynonyms() was used we use ExpandedQuery instead of the Query
                if ((currentQuery as MinShouldMatchQueryStringQuery).ExpandedQuery.IsNotNull())
                {
                    expandedQuery = (currentQuery as MinShouldMatchQueryStringQuery).ExpandedQuery;
                }
                else {
                    expandedQuery = new string[] { QueryHelpers.GetRawQueryString(currentQuery) };
                }


                foreach (var query in expandedQuery)
                {
                    var terms = QueryHelpers.GetQueryPhrases(query).Take(10);
                    var termsString = string.Join(" ", terms);

                    foreach (var fieldSelector in fieldSelectors)
                    {
                        var fieldNameLowercase = search.Client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
                        var fieldNameAnalyzed = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);                        

                        // Create PrefixQuery for single term queries larger than 2 characters
                        if (terms.Count() == 1 && terms.First().Length > 2)
                        {
                            var prefixQuery = new PrefixQuery(fieldNameLowercase, termsString.ToLower()) { Boost = PREFIXQUERY_DEFAULT_BOOST };
                            newBoolQuery.Should.Add(prefixQuery);
                        }

                        // Create PhraseQuery and PhrasePrefixQuery for multiple term queries                                              
                        if (terms.Count() > 1)
                        {
                            var phraseQuery = new PhraseQuery(fieldNameAnalyzed, termsString) { Boost = PHRASEPREFIXQUERY_DEFAULT_BOOST };
                            var phrasePrefixQuery = new PhrasePrefixQuery(fieldNameLowercase, termsString.ToLower()) { Boost = PHRASEQUERY_DEFAULT_BOOST };
                            newBoolQuery.Should.Add(phraseQuery);
                            newBoolQuery.Should.Add(phrasePrefixQuery);
                        }

                    }

                }

                log.DebugFormat("Added PrefixQuery, PhraseQuery and PhrasePrefixQuery to search");
                context.RequestBody.Query = newBoolQuery;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return FuzzyMatch<TSource>(search, FUZZY_DEFAULT_BOOST, fieldSelectors);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, double boost, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                IQuery currentQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentQuery, out currentBoolQuery))
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
                    newBoolQuery.Should.Add(currentQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentQuery);
                if (query.IsNullOrEmpty())
                {
                    return;
                }

                // Do not trigger on synonym expansions which are parenthesized and not on quoted searches.
                if (QueryHelpers.IsStringParenthesized(query) || QueryHelpers.IsStringQuoted(query))
                {
                    return;
                }

                var terms = QueryHelpers.GetQueryPhrases(query);

                // Only take terms > 2 chars and take max 3 of these
                string[] candidateTerms = terms.Where(x => x.Length > 2 && x.Length <= 16)
                                               .Take(3)
                                               .Select(x => string.Format("{0}{1}", x, "~")).ToArray();

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
                    DefaultOperator = (currentQuery as QueryStringQuery).DefaultOperator,
                    MinimumShouldMatch = (currentQuery is MinShouldMatchQueryStringQuery minShouldMatchQueryStringQuery) ? minShouldMatchQueryStringQuery.MinimumShouldMatch : "100%",
                    FuzzyPrefixLength = 3,
                    Boost = boost
                };

                newBoolQuery.Should.Add(fuzzyQueryStringQuery);               
                context.RequestBody.Query = newBoolQuery;
                log.DebugFormat("Added fuzzyMatch {0} to search", fuzzyQueryString);
            });
        }
        
        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {            
            return WildcardMatch<TSource>(search, WILDCARD_DEFAULT_BOOST, doubleSided:false,  fieldSelectors);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, double boost, bool doubleSided, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                IQuery currentQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentQuery, out currentBoolQuery))
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
                    newBoolQuery.Should.Add(currentQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentQuery);
                if (query.IsNullOrEmpty())
                {
                    return;
                }

                // Do not trigger on synonym expansions which are parenthesized and not on quoted searches.
                if (QueryHelpers.IsStringParenthesized(query) || QueryHelpers.IsStringQuoted(query))
                {
                    return;
                }

                var terms = QueryHelpers.GetQueryPhrases(query);

                // Limit term size and term count
                string[] candidateTerms = terms.Where(x => x.Length > 2 && x.Length <= 16)
                                               .Take(3)
                                               .Select(x => doubleSided ? string.Format("*{0}*", x) : string.Format("{0}*", x))
                                               .ToArray();

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
                    DefaultOperator = (currentQuery as QueryStringQuery).DefaultOperator,
                    MinimumShouldMatch = (currentQuery is MinShouldMatchQueryStringQuery minShouldMatchQueryStringQuery) ? minShouldMatchQueryStringQuery.MinimumShouldMatch : "100%",
                    Boost = boost
                };

                newBoolQuery.Should.Add(wildcardQuery);                               
                context.RequestBody.Query = newBoolQuery;
                log.DebugFormat("Added wildcard {0} to search", wildcardQueryString);
            });
        }
        
        public static IQueriedSearch<TSource, QueryStringQuery> InFieldImproved<TSource, TExistingQuery>(
                this IQueriedSearch<TSource, TExistingQuery> search,
                Expression<Func<TSource, string>> fieldSelector,
                double? relativeImportance = null)
                where TExistingQuery : QueryStringQuery
        {
            fieldSelector.ValidateNotNullArgument("fieldSelector");

            return new Search<TSource, QueryStringQuery>(search, context =>
            {

                var nonLanguageField = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed((Expression)fieldSelector);
                var languageField = search.Client.Conventions.FieldNameConvention.GetFieldNameForSearch((Expression)fieldSelector, context.Language);

                AddFieldToQueryStringQuery(context, nonLanguageField, relativeImportance);
                AddFieldToQueryStringQuery(context, languageField, relativeImportance);
            });

        }

        public static void AddFieldToQueryStringQuery(ISearchContext context, string fieldNameToAdd, double? relativeImportance)
        {
            var query = context.RequestBody.Query as MultiFieldQueryStringQuery;
            if (query.IsNull())
            {
                var originalQuery = (QueryStringQuery)context.RequestBody.Query;
                query = new MultiFieldQueryStringQuery(originalQuery.Query);
                query.RawQuery = originalQuery.RawQuery;
                if (originalQuery.DefaultField.IsNotNull())
                {
                    query.Fields.Add(originalQuery.DefaultField);
                }
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
            }

            if (relativeImportance != null)
            {
                var relativeImportanceString = relativeImportance.Value.ToString();
                fieldNameToAdd = String.Format("{0}^{1}", fieldNameToAdd, relativeImportanceString);
            }

            query.Fields.Add(fieldNameToAdd);
            context.RequestBody.Query = query;
        }

    }

}