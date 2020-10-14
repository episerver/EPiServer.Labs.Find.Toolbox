using Castle.Core.Internal;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
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

                context.RequestBody.Query = query;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> UsingRelevanceImproved<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery currentBoolQuery;
                BoolQuery newBoolQuery = new BoolQuery();
                MinShouldMatchQueryStringQuery currentQueryStringQuery;

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(currentBoolQuery.Should[0], out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    currentBoolQuery = new BoolQuery();
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(context.RequestBody.Query, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery.Should.Add(currentQueryStringQuery);
                }

                // If .UsingImprovedSynonyms has not been used there is no ExpandedQuery for us then we use the query
                if (currentQueryStringQuery.ExpandedQuery.IsNull())
                {
                    currentQueryStringQuery.ExpandedQuery = new string[] { currentQueryStringQuery.Query.ToString().Replace("¤", " ") };
                }

                foreach (var query in currentQueryStringQuery.ExpandedQuery)
                {
                    var terms = QueryHelpers.GetQueryPhrases(query);

                    foreach (var fieldSelector in fieldSelectors)
                    {
                        var fieldNameLowercase = search.Client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
                        var fieldNameAnalyzed = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);

                        // Create PrefixQuery for single term queries larger than 2 characters
                        if (terms.Count() == 1 && terms.First().Length > 2)
                        {
                            var prefixQuery = new PrefixQuery(fieldNameLowercase, query.ToLower()) { Boost = 2 };
                            newBoolQuery.Should.Add(prefixQuery);
                        }

                        // Create PhraseQuery and PhrasePrefixQuery for multiple term queries                                              
                        if (terms.Count() > 1)
                        {
                            var phraseQuery = new PhraseQuery(fieldNameAnalyzed, query) { Boost = 5 };
                            var phrasePrefixQuery = new PhrasePrefixQuery(fieldNameLowercase, query.ToLower()) { Boost = 10 };
                            newBoolQuery.Should.Add(phraseQuery);
                            newBoolQuery.Should.Add(phrasePrefixQuery);
                        }

                    }

                }

                if (newBoolQuery.Should.Count == 0)
                {
                    return;
                }

                context.RequestBody.Query = newBoolQuery;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery currentBoolQuery;
                BoolQuery newBoolQuery = new BoolQuery();
                MinShouldMatchQueryStringQuery currentQueryStringQuery;

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(currentBoolQuery.Should[0], out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(context.RequestBody.Query, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery.Should.Add(currentQueryStringQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentQueryStringQuery);
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
                    DefaultOperator = currentQueryStringQuery.DefaultOperator,
                    MinimumShouldMatch = currentQueryStringQuery.MinimumShouldMatch,
                    Boost = 0.4
                };

                newBoolQuery.Should.Add(fuzzyQueryStringQuery);

                context.RequestBody.Query = newBoolQuery;

            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery currentBoolQuery;
                BoolQuery newBoolQuery = new BoolQuery();
                MinShouldMatchQueryStringQuery currentQueryStringQuery;

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(currentBoolQuery.Should[0], out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(context.RequestBody.Query, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery.Should.Add(currentQueryStringQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentQueryStringQuery);
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
                    DefaultOperator = currentQueryStringQuery.DefaultOperator,
                    MinimumShouldMatch = currentQueryStringQuery.MinimumShouldMatch,
                    Boost = 0.2
                };

                newBoolQuery.Should.Add(wildcardQuery);

                context.RequestBody.Query = newBoolQuery;
            });
        }

        private static bool IsParenthesized(string text)
        {
            return (text.StartsWith("(") && text.EndsWith(")"));
        }

    }
}