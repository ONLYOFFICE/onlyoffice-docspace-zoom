namespace ASC.ZoomService.Models
{
    public class ZoomDeauthorizationModel
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [JsonPropertyName("signature")]
        public string Signature { get; set; }

        [JsonPropertyName("deauthorization_time")]
        public DateTimeOffset DeauthorizationTime { get; set; }

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }
    }
}
