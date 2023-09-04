using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace ASC.ZoomService.Extensions
{
    public static class ZoomCacheExtensions
    {
        public static void PutOauthVerifier(this IDistributedCache cache, string challenge, string verifier)
        {
            cache.SetString(GetCacheKeyFromOauthChallenge(challenge), verifier, OAuthChallengeCacheOptions);
        }

        public static string GetOauthVerifier(this IDistributedCache cache, string challenge)
        {
            return cache.GetString(GetCacheKeyFromOauthChallenge(challenge));
        }

        public static ZoomCollaborationCachedRoom GetCollaboration(this IDistributedCache cache, string meetingId)
        {
            var json = cache.GetString(GetCacheKeyFromMeetingId(meetingId));
            return JsonSerializer.Deserialize<ZoomCollaborationCachedRoom>(json);
        }

        public static void SetCollaboration(this IDistributedCache cache, string meetingId, ZoomCollaborationCachedRoom collaboration)
        {
            var json = JsonSerializer.Serialize(collaboration);
            cache.SetString(GetCacheKeyFromMeetingId(meetingId), json, CollaborationCacheOptions);
        }

        public static void RemoveCollaboration(this IDistributedCache cache, string meetingId)
        {
            cache.Remove(meetingId);
        }

        private static string GetCacheKeyFromMeetingId(string meetingId)
        {
            return $"zoom-collab-{meetingId}";
        }

        private static string GetCacheKeyFromOauthChallenge(string challenge)
        {
            return $"zoom-oauth-challenge-{challenge}";
        }

        private static readonly DistributedCacheEntryOptions OAuthChallengeCacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
        private static readonly DistributedCacheEntryOptions CollaborationCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(20) };
    }
}
