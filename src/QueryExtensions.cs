using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Json;
using Newtonsoft.Json;

namespace EPiServer.Labs.Find.Toolbox
{
    public class MinShouldMatchQueryStringQuery : MultiFieldQueryStringQuery
    {
        public MinShouldMatchQueryStringQuery(FieldFilterValue query)
            : base(query)
        {
        }

        [JsonProperty("minimum_should_match", NullValueHandling = NullValueHandling.Ignore)]
        public string MinimumShouldMatch { get; set; }

        [JsonProperty("lenient", NullValueHandling = NullValueHandling.Ignore)]
        public bool Lenient { get; set; } = true;

        [JsonIgnore]
        public string[] QueriesForMatch { get; set; }

    }

    public class MinShouldMatchBoolQuery : BoolQuery
    {
        public MinShouldMatchBoolQuery() : base()
        {
        }

        [JsonProperty("minimum_should_match", NullValueHandling = NullValueHandling.Ignore)]
        public string MinimumShouldMatch { get; set; }

    }

    [JsonConverter(typeof(PhraseQueryConverter))]
    public class PhraseQuery : BoostableQuery
    {
        public PhraseQuery(string field, string value)
        {
            //TODO: Validate args not null
            Field = field;
            Value = value;
        }

        [JsonIgnore]
        public string Field { get; set; }

        [JsonProperty("query")]
        public string Value { get; set; }

        [JsonProperty("slop")]
        public int Slop { get { return 2; } }

    }

    public class PhraseQueryConverter : CustomWriteConverterBase<PrefixQuery>
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value.IsNot<PhraseQuery>())
            {
                writer.WriteNull();
                return;
            }
            var query = (PhraseQuery)value;

            writer.WriteStartObject();
            writer.WritePropertyName("match_phrase");
            writer.WriteStartObject();
            writer.WritePropertyName(query.Field);
            writer.WriteStartObject();
            WriteNonIgnoredProperties(writer, value, serializer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }

    [JsonConverter(typeof(PhrasePrefixQueryConverter))]
    public class PhrasePrefixQuery : BoostableQuery
    {
        public PhrasePrefixQuery(string field, string value)
        {
            //TODO: Validate args not null
            Field = field;
            Value = value;
        }

        [JsonIgnore]
        public string Field { get; set; }

        [JsonProperty("query")]
        public string Value { get; set; }
    }

    public class PhrasePrefixQueryConverter : CustomWriteConverterBase<PrefixQuery>
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value.IsNot<PhrasePrefixQuery>())
            {
                writer.WriteNull();
                return;
            }
            var query = (PhrasePrefixQuery)value;
            writer.WriteStartObject();
            writer.WritePropertyName("match_phrase_prefix");
            writer.WriteStartObject();
            writer.WritePropertyName(query.Field);
            writer.WriteStartObject();
            WriteNonIgnoredProperties(writer, value, serializer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }

}