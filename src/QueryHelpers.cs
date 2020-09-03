using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Tracing;

namespace EPiServer.Find.Helpers
{

    public static class QueryHelpers
    {

        public static bool TryGetMinShouldMatchQueryStringQuery<TSource>(IQuery query, IQueriedSearch<TSource> search, out MinShouldMatchQueryStringQuery currentMinShouldMatchQueryStringQuery)
        {
            currentMinShouldMatchQueryStringQuery = query as MinShouldMatchQueryStringQuery;
            if (currentMinShouldMatchQueryStringQuery == null)
            {
                return false;
            }

            return true;
        }

        public static bool TryGetBoolQuery<TSource>(IQuery query, IQueriedSearch<TSource> search, out BoolQuery currentBoolQuery)
        {
            currentBoolQuery = query as BoolQuery;
            if (currentBoolQuery == null)
            {
                return false;
            }

            return true;

        }

        public static bool TryGetQueryStringQuery<TSource>(IQuery query, IQueriedSearch<TSource> search, out MultiFieldQueryStringQuery currentQueryStringQuery)
        {
            currentQueryStringQuery = query as MultiFieldQueryStringQuery;
            if (currentQueryStringQuery == null)
            {
                // Synonyms are only supported for QueryStringQuery
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
        public static string GetQueryString(MultiFieldQueryStringQuery currentQueryStringQuery)
        {
            return (currentQueryStringQuery.Query ?? string.Empty).ToString();
        }

    }

}