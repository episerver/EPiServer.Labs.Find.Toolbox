using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Framework;
using EPiServer.Framework;
using EPiServer.Framework.Localization;
using EPiServer.Globalization;
using EPiServer.ServiceLocation;
using EPiServer.Shell;
using EPiServer.Shell.Search;
using EPiServer.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Caching;

namespace EPiServer.Find.Cms.SearchProviders
{
    [SearchProvider]
    public class ToolboxBlockSearchProvider : EnterpriseBlockSearchProvider
    {
        private readonly UIDescriptorRegistry _uiDescriptorRegistry;
        private const string AllowedTypes = "allowedTypes";
        private const string RestrictedTypes = "restrictedTypes";

        public override string Area => FindContentSearchProviderConstants.BlockArea;
        public override string Category => "Find Toolbox - Blocks";

        public ToolboxBlockSearchProvider(LocalizationService localizationService,
                ISiteDefinitionResolver siteDefinitionResolver, IContentTypeRepository contentTypeRepository,
                UIDescriptorRegistry uiDescriptorRegistry)
                : base(localizationService, siteDefinitionResolver, contentTypeRepository, uiDescriptorRegistry)
        {
            _uiDescriptorRegistry = uiDescriptorRegistry;
        }

        public override IEnumerable<SearchResult> Search(Query query)
        {
            Validator.ThrowIfNull("SearchProviderFactory.Instance.AccessFilter", FilterFactory.Instance.ContentAccessFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.CultureFilter", FilterFactory.Instance.CultureFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.RootsFilter", FilterFactory.Instance.RootsFilter);

            query.MaxResults = 20;

            ITypeSearch<IContentData> searchQuery = GetFieldQuery(query.SearchQuery, query.MaxResults)
                .Filter(x => x.MatchTypeHierarchy(typeof(BlockData)));

            var allowedTypes = GetContentTypesFromQuery(AllowedTypes, query);
            var restrictedTypes = GetContentTypesFromQuery(RestrictedTypes, query);

            FilterContext filterContext = FilterContext.Create<BlockData, ContentType>(query);
            searchQuery = FilterFactory.Instance.AllowedTypesFilter(searchQuery, filterContext, allowedTypes);
            searchQuery = FilterFactory.Instance.RestrictedTypesFilter(searchQuery, filterContext, restrictedTypes);
            searchQuery = FilterFactory.Instance.ContentAccessFilter(searchQuery, filterContext);
            searchQuery = FilterFactory.Instance.CultureFilter(searchQuery, filterContext);
            searchQuery = FilterFactory.Instance.RootsFilter(searchQuery, filterContext);

            var cacheSettings = ServiceLocator.Current.GetInstance<IContentCacheKeyCreator>();

            using (var cacheDependancy = new CacheDependency(null, new[] { cacheSettings.RootKeyName }))
            {
                var contentLinksWithLanguage = searchQuery
                    .Select(x =>
                            new ContentInLanguageReference(
                                new ContentReference(((IContent)x).ContentLink.ID,
                                                     ((IContent)x).ContentLink.ProviderName),
                                ((ILocalizable)x).Language.Name))
                    .StaticallyCacheFor(TimeSpan.FromMinutes(1), cacheDependancy)
                    .GetResult();

                BlockData content = null;
                return contentLinksWithLanguage
                    .Where(
                        searchResult =>
                        DataFactory.Instance.TryGet<BlockData>(searchResult.ContentLink,
                                                               !String.IsNullOrEmpty(searchResult.Language)
                                                                   ? new LanguageSelector(searchResult.Language)
                                                                   : LanguageSelector.AutoDetect(true), out content))
                    .Select(item => CreateSearchResult(content));
            }
        }

        private new ITypeSearch<IContentData> GetFieldQuery(string SearchQuery, int maxResults)
        {
            var language = ResolveSupportedLanguageBasedOnPreferredCulture(SearchClient.Instance);

            if (String.IsNullOrEmpty(SearchQuery))
            {
                SearchQuery = "*";
            }

            var query = SearchClient.Instance.Search<IContentData>(language)
                .For(SearchQuery)
                .InField(x => ((IContent)x).Name, 10)
                .InField(x => ((IContent)x).ContentTypeName(), 2)
                .InField(x => x.SearchText());

            if (int.TryParse(SearchQuery, out int parsedQuery))
            {
                query = query.InField(x => ((IContent)x).ContentLink.ID, 10);
            }

            query = AddContentSpecificFields(query);
            query = AddFindToolboxQueries(query);

            return query
                .Take(maxResults).SetTimeout(10000);
        }

        private IEnumerable<Type> GetContentTypesFromQuery(string parameter, Query query)
        {
            if (query.Parameters.ContainsKey(parameter))
            {
                var array = query.Parameters[parameter] as JArray;
                if (array != null)
                {
                    return array.Values<string>().SelectMany(GetContentTypes);
                }
            }
            return Enumerable.Empty<Type>();
        }

        private new IEnumerable<Type> GetContentTypes(string allowedType)
        {
            var uiDescriptor = _uiDescriptorRegistry.UIDescriptors.FirstOrDefault(d => d.TypeIdentifier.Equals(allowedType, StringComparison.OrdinalIgnoreCase));
            if (uiDescriptor == null)
                return Enumerable.Empty<Type>();

            return _contentTypeRepository
                .List()
                .Where(c => uiDescriptor.ForType.IsAssignableFrom(c.ModelType))
                .Select(c => c.ModelType);
        }

        private Language ResolveSupportedLanguageBasedOnPreferredCulture(IClient client)
        {
            Language language = null;
            var preferredCulture = ContentLanguage.PreferredCulture;
            if (preferredCulture != null)
            {
                language = client.Settings.Languages.GetSupportedLanguage(preferredCulture);
            }
            language = language ?? Language.None;
            return language;
        }

        private IQueriedSearch<IContentData, QueryStringQuery> AddFindToolboxQueries(IQueriedSearch<IContentData, QueryStringQuery> query)
        {
            query = query
                    .MinimumShouldMatch("1")
                    .UsingSynonymsImproved()
                    .UsingRelevanceImproved(x => ((IContent)x).Name, x => x.SearchText(), x => ((IContent)x).ContentTypeName())
                    .WildcardMatch(x => ((IContent)x).Name, x => ((IContent)x).ContentTypeName())
                    .FuzzyMatch(x => ((IContent)x).Name, x => ((IContent)x).ContentTypeName());                   

            return query;
        }

    }
}