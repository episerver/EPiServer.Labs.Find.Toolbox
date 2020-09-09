using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Helpers;
using EPiServer.ServiceLocation;
using System;
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


        public static IQueriedSearch<TSource, QueryStringQuery> PhraseBoost<TSource>(this IQueriedSearch<TSource> search, double? relativeImportance = null, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery currentBoolQuery;
                BoolQuery newBoolQuery = new BoolQuery();
                MultiFieldQueryStringQuery currentQueryStringQuery;

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, search, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetQueryStringQuery(currentBoolQuery.Should[0], search, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    currentBoolQuery = new BoolQuery();
                    if (!QueryHelpers.TryGetQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
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

                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);
                    var phraseQuery = new PhraseQuery(fieldName, query);
                    phraseQuery.Boost = relativeImportance;
                    newBoolQuery.Should.Add(phraseQuery);
                }

                if (newBoolQuery.Should.Count == 0)
                {
                    return;
                }

                context.RequestBody.Query = newBoolQuery;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> PrefixBoost<TSource>(this IQueriedSearch<TSource> search, double? relativeImportance = null, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery currentBoolQuery;
                BoolQuery newBoolQuery = new BoolQuery();
                MultiFieldQueryStringQuery currentQueryStringQuery;

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, search, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetQueryStringQuery(currentBoolQuery.Should[0], search, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    currentBoolQuery = new BoolQuery();
                    if (!QueryHelpers.TryGetQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
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

                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions.FieldNameConvention.GetFieldName(fieldSelector);
                    var phraseQuery = new PrefixQuery(fieldName, query);
                    phraseQuery.Boost = relativeImportance;
                    newBoolQuery.Should.Add(phraseQuery);
                }

                if (newBoolQuery.Should.Count == 0)
                {
                    return;
                }

                context.RequestBody.Query = newBoolQuery;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> PhrasePrefixBoost<TSource>(this IQueriedSearch<TSource> search, double? relativeImportance = null, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery currentBoolQuery;
                BoolQuery newBoolQuery = new BoolQuery();
                MultiFieldQueryStringQuery currentQueryStringQuery;

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, search, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetQueryStringQuery(currentBoolQuery.Should[0], search, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    currentBoolQuery = new BoolQuery();
                    if (!QueryHelpers.TryGetQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
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

                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
                    var phraseQuery = new PhrasePrefixQuery(fieldName, query);                    
                    phraseQuery.Boost = relativeImportance;
                    newBoolQuery.Should.Add(phraseQuery);
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

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, search, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(currentBoolQuery.Should[0], search, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    if (!QueryHelpers.TryGetMinShouldMatchQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
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

                var terms = QueryHelpers.GetQueryPhrases(query);
                
                // If there are no terms >2 chars then don't create a WildcardQuery
                if (terms.Where(x => x.Length > 2).Count() == 0)
                {
                    return;
                }

                var fuzzyQueryString = string.Join(" ", terms.Select(x => { return x.Length > 2 ? string.Format("{0}~", x) : x; }));

                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions
                    .FieldNameConvention
                    .GetFieldNameForAnalyzed(fieldSelector);

                    var fuzzyQueryStringQuery = new MinShouldMatchQueryStringQuery(fuzzyQueryString)
                    {
                        Fields = new[] { fieldName },
                        DefaultOperator = currentQueryStringQuery.DefaultOperator,
                        MinimumShouldMatch = currentQueryStringQuery.MinimumShouldMatch,
                        Boost = 0.2
                    };

                    newBoolQuery.Should.Add(fuzzyQueryStringQuery);

                }

                if (newBoolQuery.Should.Count == 0)
                {
                    return;
                }

                context.RequestBody.Query = newBoolQuery;

            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery currentBoolQuery;
                BoolQuery newBoolQuery = new BoolQuery();
                MultiFieldQueryStringQuery currentQueryStringQuery;

                if (QueryHelpers.TryGetBoolQuery(context.RequestBody.Query, search, out currentBoolQuery))
                {
                    if (!QueryHelpers.TryGetQueryStringQuery(currentBoolQuery.Should[0], search, out currentQueryStringQuery))
                    {
                        return;
                    }
                    newBoolQuery = currentBoolQuery;
                }
                else
                {
                    if (!QueryHelpers.TryGetQueryStringQuery(context.RequestBody.Query, search, out currentQueryStringQuery))
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

                var terms = QueryHelpers.GetQueryPhrases(query);

                // If there are no terms >2 chars then don't create a WildcardQuery
                if (terms.Where(x => x.Length > 2).Count() == 0)
                {
                    return;
                }

                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions
                        .FieldNameConvention
                        .GetFieldNameForAnalyzed(fieldSelector);

                    foreach (var term in terms)
                    {               
                        // Only add a wildcard to terms >2 chars
                        var wildcardQuery = new WildcardQuery(fieldName, string.Format("{0}{1}", term, term.Length > 2 ? "*" :""))
                        {
                            Boost = 0.2
                        };
                        newBoolQuery.Should.Add(wildcardQuery);                 
                    }
                }

                if (newBoolQuery.Should.Count == 0)
                {
                    return;
                }

                context.RequestBody.Query = newBoolQuery;
            });
        }

    }
}