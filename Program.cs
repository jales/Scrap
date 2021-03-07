using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;

namespace Scrap
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Scrape all in parallel
            await Task.WhenAll(args.Select(async symbol => await ScrapeSymbol(symbol)));
        }

        private static async Task ScrapeSymbol(string symbol)
        {
            try
            {
                using var client = CreateHttpClient();

                var (canvass, fetchrConfiguration) = await DownloadSymbolConfiguration(symbol, client);

                var responseContent = await DownloadCanvassInformation(canvass, fetchrConfiguration, client);

                string conversations = canvass.comments["canvass-0-CanvassApplet"].count;
                // This seems to be an alternative to the above but the numbers are inconsistent
                //string conversations = responseContent.g0.data.messageCount;

                string reacting = responseContent.g0.data.typingUsersCount;

                string viewing = responseContent.g0.data.readingUsersCount;

                Console.WriteLine($"{symbol}: {conversations} conversation(s); {reacting} person(s) reacting; {viewing} viewing");
            }
            catch (Exception e)
            {
                Console.WriteLine($"{symbol}: Failed to scrape: {e.Message}");
            }
        }

        private static async Task<(dynamic,  dynamic)> DownloadSymbolConfiguration(string symbol, HttpClient client)
        {
            using var response = await client.GetAsync($"/quote/{symbol}/community?p={symbol}&guccounter=1");

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();

            var appDefinition = responseContent
               .Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .First(l => l.StartsWith("root.App.main"));

            dynamic appContent = JObject.Parse(appDefinition[appDefinition.IndexOf('{')..^1]);

            dynamic canvass = appContent.context.dispatcher.stores.CanvassStore;
            dynamic fetchrConfiguration = appContent.context.plugins.FetchrPlugin;

            return (canvass, fetchrConfiguration);
        }

        private static async Task<dynamic> DownloadCanvassInformation(dynamic canvass, dynamic fetchrConfiguration, HttpClient client)
        {
            string path = fetchrConfiguration.xhrPath;
            string crumb = fetchrConfiguration.xhrContext.crumb;
            var url = $"{path}?crumb={HttpUtility.UrlEncode(crumb)}";

            string context = canvass.comments["canvass-0-CanvassApplet"].context;
            var request = $@"{{
  ""requests"": {{
    ""g0"": {{
      ""resource"": ""canvass.getPresence_ns"",
      ""operation"": ""read"",
      ""params"": {{
        ""apiVersion"": ""v1"",
        ""context"": ""{context}"",
        ""namespace"": ""yahoo_finance"",
        ""oauthConsumerKey"": ""finance.oauth.client.canvass.prod.consumerKey"",
        ""oauthConsumerSecret"": ""finance.oauth.client.canvass.prod.consumerSecret""
      }}
    }}
  }}
}}";

            using var response = await client.PostAsync(url, new StringContent(request, Encoding.UTF8, "application/json"));
            response.EnsureSuccessStatusCode();

            dynamic responseContent = JObject.Parse(await response.Content.ReadAsStringAsync());
            return responseContent;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpMessageHandler handler = new HttpClientHandler
            {
                // Enable cookie sharing between requests
                CookieContainer = new CookieContainer()
            };

            return new HttpClient(handler, true)
            {
                // Set the default base address for all requests
                BaseAddress = new Uri("https://finance.yahoo.com"),
            };
        }
    }
}
