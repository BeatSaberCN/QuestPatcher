using System.Net.Http;
using System.Net.Http.Headers;

namespace QuestPatcher.Core.Utils
{
    public static class WebUtils
    {
        private static readonly ProductInfoHeaderValue UserAgent =
            ProductInfoHeaderValue.Parse($"QuestPatcher/{VersionUtil.QuestPatcherVersion}");

        private static readonly MediaTypeWithQualityHeaderValue JsonMediaType =
            MediaTypeWithQualityHeaderValue.Parse("application/json");

        public static HttpClient HttpClient
        {
            get
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
                client.DefaultRequestHeaders.Accept.Add(JsonMediaType);
                return client;
            }
        }
    }
}
