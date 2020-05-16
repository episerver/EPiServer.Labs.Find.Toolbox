using Newtonsoft.Json;

namespace EPiServer.Find.Api.Querying.Queries
{
    public class MinShouldMatchQueryStringQuery : MultiFieldQueryStringQuery
    {
        public MinShouldMatchQueryStringQuery(FieldFilterValue query)
            : base(query)
        {
        }

        [JsonProperty("minimum_should_match", NullValueHandling = NullValueHandling.Ignore)]
        public string MinimumShouldMatch { get; set; }
    }
}