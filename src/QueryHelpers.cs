using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Tracing;
using System.Linq;
using System.Text.RegularExpressions;

namespace EPiServer.Find
{

    public static class QueryHelpers
    {

        // Return all terms and phrases in query
        public static string[] GetQueryPhrases(string query)
        {
            // Replace double space, tabs with single whitespace and trim space on side
            string cleanedQuery = Regex.Replace(UnescapeElasticSearchQuery(query), @"\s+", " ").Trim();

            // Match single terms and quoted terms, allow hyphens and ´'` in terms, allow space between quotes and word.
            return Regex.Matches(cleanedQuery, @"([\w-]+)|([""][\s\w-´'`]+[""])").Cast<Match>().Select(c => c.Value.Trim()).Except(new string[] { "AND", "OR" }).Take(50).ToArray();
        }

        public static string UnescapeElasticSearchQuery(string s)
        {
            return s.Replace("\\", "");
        }

        public static string EscapeElasticSearchQuery(string s)
        {
            return s.Replace("-", "\\-");
        }

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

        public static string GetRawQueryString(MultiFieldQueryStringQuery currentQueryStringQuery)
        {
            return (currentQueryStringQuery.RawQuery ?? string.Empty).ToString();
        }

    }

}