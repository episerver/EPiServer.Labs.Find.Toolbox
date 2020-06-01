using EPiServer.Find.Api.Querying.Queries;
using EPiServer.ServiceLocation;
using System;

namespace EPiServer.Find.Cms
{

    public static class MultiFieldQueryStringQueryExtensions
    {
        private static Lazy<UsingSynonymService> _lazyUsingSynonymService = new Lazy<UsingSynonymService>(() => ServiceLocator.Current.GetInstance<UsingSynonymService>());

        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search)
        {
            return UsingSynonymsImproved(search, _lazyUsingSynonymService.Value);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, UsingSynonymService usingSynonymService)
        {
            return usingSynonymService.UsingSynonyms(search);
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
    }
}