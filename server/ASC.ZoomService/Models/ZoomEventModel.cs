namespace ASC.ZoomService.Models
{
    public class ZoomEventModel<T> where T : class
    {
        [JsonPropertyName("event")]
        public string Event { get; set; }

        [JsonPropertyName("event_ts")]
        public long EventTs { get; set; }

        [JsonPropertyName("payload")]
        public T Payload { get; set; }
    }
}
