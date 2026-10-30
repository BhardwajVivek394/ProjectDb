using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Lets you store any object in ASP.NET Core session via JSON serialization.
    /// Core session only supports string/int/byte[] natively.
    /// </summary>
    public static class SessionExtensions
    {
        public static void SetObject<T>(this ISession session, string key, T value)
            => session.SetString(key, JsonSerializer.Serialize(value));

        public static T? GetObject<T>(this ISession session, string key)
        {
            var json = session.GetString(key);
            return json == null ? default : JsonSerializer.Deserialize<T>(json);
        }
    }
}
