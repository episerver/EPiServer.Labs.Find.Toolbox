using System.Collections.Generic;
using EPiServer.Web;
using EPiServer.Shell.Search;
using EPiServer.Framework.Localization;
using EPiServer.DataAbstraction;
using EPiServer.Shell;
using EPiServer.Framework;
using EPiServer.Find;
using EPiServer.Core;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Globalization;
using EPiServer.ServiceLocation;
using System;
using EPiServer.Find.Framework;
using System.Linq;
using Newtonsoft.Json.Linq;
using EPiServer.Find.UnifiedSearch;
using EPiServer.Web.Routing;
using EPiServer.Find.Cms;
using EPiServer.Find.Cms.SearchProviders;
using EPiServer.Cms.Shell.UI.UIDescriptors.ViewConfigurations.Internal;
using EPiServer.Editor;
using EPiServer.Find.Api;
using Microsoft.AspNetCore.Http;

namespace EPiServer.Labs.Find.Toolbox.SearchProviders
{

    [SearchProvider]
    public class ToolboxPageSearchProvider : EnterprisePageSearchProvider
    {

        private readonly UIDescriptorRegistry _uiDescriptorRegistry;
        private readonly IContentRepository _contentRepository;
        private const string AllowedTypes = "allowedTypes";
        private const string RestrictedTypes = "restrictedTypes";

        public override string Area => FindContentSearchProviderConstants.PageArea;
        public override string Category => "Find Toolbox - Pages";

        public ToolboxPageSearchProvider(LocalizationService localizationService,
            ISiteDefinitionResolver siteDefinitionResolver,
            IContentTypeRepository<PageType> contentTypeRepository,
            UIDescriptorRegistry uiDescriptorRegistry,
            EditUrlResolver editUrlResolver,
            ServiceAccessor<SiteDefinition> currentSiteDefinition,
            IContentLanguageAccessor languageResolver,
            IUrlResolver urlResolver,
            ITemplateResolver templateResolver,
            IContentRepository contentRepository)
            : base(localizationService, siteDefinitionResolver, contentTypeRepository, uiDescriptorRegistry, editUrlResolver, currentSiteDefinition, languageResolver, urlResolver, templateResolver, contentRepository)
        {
            _contentRepository = contentRepository;
            _uiDescriptorRegistry = uiDescriptorRegistry;
        }

        public override IEnumerable<SearchResult> Search(Query query)
        {
            Validator.ThrowIfNull("SearchProviderFactory.Instance.AccessFilter", FilterFactory.Instance.ContentAccessFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.CultureFilter", FilterFactory.Instance.CultureFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.RootsFilter", FilterFactory.Instance.RootsFilter);

            query.MaxResults = 20;

            ITypeSearch<IContentData> searchQuery = GetFieldQuery(query.SearchQuery, query.MaxResults)
                .Filter(x => x.MatchTypeHierarchy(typeof(IContentData)));

            var allowedTypes = GetContentTypesFromQuery(AllowedTypes, query);
            var restrictedTypes = GetContentTypesFromQuery(RestrictedTypes, query);

            FilterContext filterContext = FilterContext.Create<IContentData, ContentType>(query);
            searchQuery = FilterFactory.Instance.AllowedTypesFilter(searchQuery, filterContext, allowedTypes);
            searchQuery = FilterFactory.Instance.RestrictedTypesFilter(searchQuery, filterContext, restrictedTypes);
            searchQuery = FilterFactory.Instance.ContentAccessFilter(searchQuery, filterContext);
            searchQuery = FilterFactory.Instance.CultureFilter(searchQuery, filterContext);
            searchQuery = FilterFactory.Instance.RootsFilter(searchQuery, filterContext);

            var cacheSettings = ServiceLocator.Current.GetInstance<IContentCacheKeyCreator>();

            var contentLinksWithLanguage = searchQuery
                .Select(x =>
                        new ContentInLanguageReference(
                            new ContentReference(((IContent)x).ContentLink.ID,
                                                 ((IContent)x).ContentLink.ProviderName),                                             
                            ((ILocalizable)x).Language.Name))
                .StaticallyCacheFor(TimeSpan.FromMinutes(10), null, cacheSettings.RootKeyName)
            .GetResult();


            PageData content = null;
            return contentLinksWithLanguage
                .Where(
                    searchResult =>
                    _contentRepository.TryGet(searchResult.ContentLink,
                                                           !string.IsNullOrEmpty(searchResult.Language)
                                                               ? new LanguageSelector(searchResult.Language)
                                                               : LanguageSelector.AutoDetect(true), out content))
                     .Select(item => CreateSearchResult(content));

        }

        private new ITypeSearch<IContentData> GetFieldQuery(string SearchQuery, int maxResults)
        {

            var language = ResolveSupportedLanguageBasedOnPreferredCulture(SearchClient.Instance);

            if (string.IsNullOrEmpty(SearchQuery))
            {
                SearchQuery = "*";
            }

            var query = SearchClient.Instance.Search<IContentData>(language)
                .For(SearchQuery);

            if (SearchQuery.IsAbsoluteUrl())
            {
                query = query.InField(x => ((ISearchContent)x).SearchHitUrl);
                return query
                .Take(maxResults);
            }

            query = query.InField(x => ((IContent)x).Name, 10)
                         .InField(x => x.SearchText(), 1)
                         .InField(x => ((IContent)x).ContentTypeName(), 0.5);
                         

            int parsedQuery;
            if (int.TryParse(SearchQuery, out parsedQuery))
            {
                query = query.InField(x => ((IContent)x).ContentLink.ID, 20);
            }

            DateTime parsedDate;
            if (DateTime.TryParse(SearchQuery, out parsedDate))
            {
                query = query.InField(x => ((ISearchContent)x).SearchPublishDate.ToString(),3);
            }

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
