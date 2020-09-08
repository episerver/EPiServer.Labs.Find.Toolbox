using Castle.DynamicProxy.Generators.Emitters.SimpleAST;
using EPiServer.Data.Dynamic.Linq;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Json;
using EPiServer.Find.Tracing;
using EPiServer.ServiceLocation;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

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
                    string fieldName = search.Client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
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

                var terms = string.Join(" ", query.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length > 2));

                if (terms.IsNullOrEmpty())
                {
                    return;
                }

                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions
                    .FieldNameConvention
                    .GetFieldNameForLowercase(fieldSelector);

                    var fuzzyQueryStringQuery = new MinShouldMatchQueryStringQuery(string.Format("{0}~", terms))
                    {
                        Fields = new[] { fieldName },
                        DefaultOperator = currentQueryStringQuery.DefaultOperator,
                        MinimumShouldMatch = currentQueryStringQuery.MinimumShouldMatch,
                        Boost = 0.8
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

        /*        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyQuery<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
                {
                    return new Search<TSource, QueryStringQuery> (search, context =>
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

                        var words = query.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);                

                        foreach (var fieldSelector in fieldSelectors)
                        {
                            string fieldName = search.Client.Conventions
                                .FieldNameConvention
                                .GetFieldNameForAnalyzed(fieldSelector);

                            foreach (var word in words)
                            {
                                if (word.Length > 2)
                                {
                                    var fuzzyQuery = new FuzzyQuery(fieldName, word)
                                    {
                                        MinSimilarity = 2                                
                                    };

                                    newBoolQuery.Should.Add(fuzzyQuery);
                                }
                            }
                        }


                        if (newBoolQuery.Should.Count == 0)
                        {
                            return;
                        }

                        context.RequestBody.Query = newBoolQuery;
                    });
                }*/


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

                var terms = query.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (terms.Count == 0)
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
                        if (term.Length > 2)
                        {
                            var wildcardQuery = new WildcardQuery(fieldName, string.Format("{0}*", term))
                            {
                                Boost = 0.5
                            };
                            newBoolQuery.Should.Add(wildcardQuery);
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

    }
}