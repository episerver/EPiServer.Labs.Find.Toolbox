using EPiServer.Find;
using EPiServer.Find.Cms;
using EPiServer.Find.Connection;
using EPiServer.Find.Framework.Statistics;
using EPiServer.Find.Optimizations;
using EPiServer.Find.Optimizations.Synonyms.Api;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Labs.Find.Toolbox
{
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class SynonymLoader
    {
        private readonly IClient _client;
        private readonly IStatisticTagsHelper _statisticTagsHelper;
        private static ILogger log = LogManager.GetLogger(typeof(SearchRequestExtensions));

        public SynonymLoader(IClient client, IStatisticTagsHelper statisticTagsHelper)
        {
            _client = client;
            _statisticTagsHelper = statisticTagsHelper;
        }

        public Dictionary<string, HashSet<string>> GetSynonyms(TimeSpan? cacheDuration = null)
        {
            return GetSynonyms(100, new RuntimeCacheAdapter(new MemoryCache(new MemoryCacheOptions())), new StaticCachePolicy(cacheDuration == null ? DateTime.Now.AddHours(1) : DateTime.Now.Add((TimeSpan)cacheDuration)));
        }

        public Dictionary<string, HashSet<string>> GetSynonyms(int synonymBatchSize, RuntimeCacheAdapter cache, StaticCachePolicy staticCachePolicy)
        {
            var statisticLanguageTags = GetStatisticLanguageTags();
            string synonymCacheKey = GetSynonymCacheKey(statisticLanguageTags);

            Dictionary<string, HashSet<string>> synonymsCached;
            if (TryGetCachedSynonym(synonymCacheKey, cache, out synonymsCached))
            {
                log.Debug("Loaded {0} synonyms from cache (duration: {1}) for tags {2}", synonymsCached.Count, staticCachePolicy.ExpirationDate.ToString(), string.Join(",", statisticLanguageTags));
                return synonymsCached;
            }

            var loadedSynonyms = LoadSynonymsFromSourceIndex(synonymBatchSize, statisticLanguageTags);
            var synonymDictionary = CreateSynonymDictionary(loadedSynonyms);
            cache.AddOrUpdate(synonymCacheKey, staticCachePolicy, synonymDictionary);

            log.Debug("Loaded {0} synonyms from index (duration: {1}) for tags {2}", loadedSynonyms.Count(), staticCachePolicy.ExpirationDate.ToString(), string.Join(",", statisticLanguageTags));
            return synonymDictionary;
        }

        /// <summary>
        /// Get current language and all-languages languages to filter and skip SiteID as it's not stored with synonyms
        /// </summary>
        /// <returns></returns>
        private IEnumerable<string> GetStatisticLanguageTags()
        {
            var statTags = _statisticTagsHelper.GetTags(true);
            return statTags.Where(x => x.Contains("language"));
        }

        private string GetSynonymCacheKey(IEnumerable<string> statisticLanguageTags)
        {
            return string.Format("FindSynonymList_" + string.Join(",", statisticLanguageTags));
        }

        private bool TryGetCachedSynonym(string synonymCacheKey, RuntimeCacheAdapter cache, out Dictionary<string, HashSet<string>> synonymsCached)
        {
            synonymsCached = cache.Get<Dictionary<string, HashSet<string>>>(synonymCacheKey);
            if (synonymsCached == null)
            {
                return false;
            }

            return true;
        }

        private IEnumerable<Synonym> LoadSynonymsFromSourceIndex(int synonymBatchSize, IEnumerable<string> statisticLanguageTags)
        {
            var synonyms = new List<Synonym>();
            int retries = 0;
            var page = 0;
            while (true)
            {
                try
                {
                    var result = _client.Optimizations().Synonyms().List(synonymBatchSize, page * synonymBatchSize, statisticLanguageTags);
                    var hits = result.Hits.Count();
                    if (hits > 0) { synonyms.AddRange(result.Hits); }
                    if (hits < synonymBatchSize) // Not a full batch indicates last batch
                    {
                        break;
                    }
                    page++;
                    retries = 0;
                }
                catch
                {
                    //Attempt a few retries
                    retries++;
                    if (retries > 2) // Bail out if on 3 failed attempts
                    {
                        break;
                    }
                }
            }

            return synonyms;
        }

        /// <summary>
        /// Here we flatten and simplify the synonym list structure
        /// For every multiple phrase (terms separated by comma) we generate a new synonym pair
        /// For every birectional we generate a new synonym pair reversed
        /// For every multiple term we generate a new quoted variant
        /// </summary>
        /// <returns></returns>
        private Dictionary<string, HashSet<string>> CreateSynonymDictionary(IEnumerable<Synonym> loadedSynonyms)
        {
            var synonymsFlattened = new Dictionary<string, HashSet<string>>();
            foreach (var synonym in loadedSynonyms)
            {

                var multiplePhrases = synonym.Phrase.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

                foreach (var singlePhrase in multiplePhrases)
                {
                    AddSynonym(singlePhrase, synonym.SynonymPhrase, ref synonymsFlattened);

                    // If multiple terms, add a new pair as a quoted phrase
                    if (ContainsMultipleTerms(singlePhrase))
                    {
                        AddSynonym(string.Format("\"{0}\"", singlePhrase), synonym.SynonymPhrase, ref synonymsFlattened);
                    }

                    // Birectional synonym, add a new pair reversed 
                    if (synonym.Bidirectional)
                    {
                        AddSynonym(synonym.SynonymPhrase, singlePhrase, ref synonymsFlattened);

                        // If multiple terms, add a new pair as a quoted phrase
                        if (ContainsMultipleTerms(synonym.SynonymPhrase))
                        {
                            AddSynonym(string.Format("\"{0}\"", synonym.SynonymPhrase), singlePhrase, ref synonymsFlattened);
                        }
                    }

                }

            }

            return synonymsFlattened;
        }

        private static void AddSynonym(string phrase, string synonym, ref Dictionary<string, HashSet<string>> synonyms)
        {
            if (synonyms.TryGetValue(phrase, out HashSet<string> existingSynonym))
            {
                existingSynonym.Add(synonym.ToLowerInvariant());
                synonyms[phrase] = existingSynonym;
            }
            else
            {
                synonyms.Add(phrase, new HashSet<string>() { synonym });
            }
        }

        private static bool ContainsMultipleTerms(string text)
        {
            return text.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count() > 1;
        }
    }
}