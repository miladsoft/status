using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StatusChecker
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static string discordWebhookUrl = "";

        static async Task Main(string[] args)
        {
            InitializeSettings(args);

            string servicesFilePath = Path.Combine("docs", "services.json");
            if (!File.Exists(servicesFilePath))
            {
                Console.WriteLine("services.json file not found.");
                return;
            }

            string servicesJson = await File.ReadAllTextAsync(servicesFilePath);
            JObject apps = JObject.Parse(servicesJson);

            string statusFilePath = Path.Combine("docs", "status.json");
            JObject statusData = new JObject();

            // Load existing status data or create a new file if it does not exist
            if (File.Exists(statusFilePath))
            {
                string existingStatusJson = await File.ReadAllTextAsync(statusFilePath);
                statusData = JObject.Parse(existingStatusJson);
            }
            else
            {
                Console.WriteLine("status.json file not found. Creating a new one.");
                statusData = new JObject();
            }

            foreach (var app in apps["apps"])
            {
                string appName = app["name"]?.ToString() ?? "Unknown";
                Console.WriteLine($"Checking services for app: {appName}");

                // Create or get app's status array
                if (statusData[appName] == null)
                {
                    statusData[appName] = new JArray();
                }

                JArray appStatusArray = (JArray)statusData[appName];

                // Check website status
                await CheckAndUpdateService(app["website"], appStatusArray);

                // Check each service, indexer, and relay status
                foreach (var service in app["services"] ?? new JArray())
                {
                    await CheckAndUpdateService(service, appStatusArray);
                }

                foreach (var indexer in app["indexers"] ?? new JArray())
                {
                    await CheckAndUpdateService(indexer, appStatusArray);
                }

                foreach (var relay in app["relays"] ?? new JArray())
                {
                    await CheckAndUpdateService(relay, appStatusArray);
                }

                // Maintain only last 600 entries for the app
                if (appStatusArray.Count > 600)
                {
                    appStatusArray = new JArray(appStatusArray.Skip(appStatusArray.Count - 600));
                    statusData[appName] = appStatusArray;
                }
            }

            // Save status data to the file
            await File.WriteAllTextAsync(statusFilePath, statusData.ToString(Formatting.Indented));
            Console.WriteLine("Status check completed and saved.");
        }

        private static async Task CheckAndUpdateService(JToken service, JArray appStatusArray)
        {
            string serviceName = service["name"]?.ToString() ?? "Unknown";
            string serviceUrl = service["url"]?.ToString() ?? string.Empty;
            string serviceType = service["type"]?.ToString() ?? "Unknown";

            if (string.IsNullOrEmpty(serviceUrl))
            {
                Console.WriteLine($"URL for service '{serviceName}' is missing.");
                return;
            }

            bool isActive = await CheckServiceStatus(serviceUrl);

            var statusEntry = new JObject
            {
                ["name"] = serviceName,
                ["url"] = serviceUrl,
                ["type"] = serviceType,
                ["isActive"] = isActive,
                ["timestamp"] = DateTime.UtcNow
            };

            appStatusArray.Add(statusEntry);

            if (!isActive)
            {
                await SendToDiscord(serviceName, serviceUrl, serviceType);
            }
        }

        private static async Task<bool> CheckServiceStatus(string url)
        {
            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking status for {url}: {ex.Message}");
                return false;
            }
        }

        private static async Task SendToDiscord(string serviceName, string serviceUrl, string serviceType)
        {
            if (string.IsNullOrEmpty(discordWebhookUrl))
            {
                Console.WriteLine("Discord webhook URL is not provided.");
                return;
            }

            var messageContent = new
            {
                content = $"Service **{serviceName}** (Type: {serviceType}) at {serviceUrl} is currently **down**. Time: {DateTime.UtcNow}"
            };

            var jsonContent = new StringContent(JsonConvert.SerializeObject(messageContent), Encoding.UTF8, "application/json");

            try
            {
                var response = await httpClient.PostAsync(discordWebhookUrl, jsonContent);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Discord notification sent for {serviceName}.");
                }
                else
                {
                    Console.WriteLine($"Failed to send Discord notification for {serviceName}. Status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while sending Discord notification: {ex.Message}");
            }
        }

        private static void InitializeSettings(string[] args)
        {
            discordWebhookUrl = ExtractArgument(args, "-webhook");
        }

        private static string? ExtractArgument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
