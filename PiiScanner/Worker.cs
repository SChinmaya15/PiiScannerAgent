using PiiScanner.Models;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiiScanner
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly HttpClient _httpClient;
        private readonly Scan _scan;
        private readonly IConfiguration _configuration;
        private readonly string _configPath;
        private int _pollingIntervalMinutes =5;

        public Worker(ILogger<Worker> logger,Scan scan, HttpClient httpClient, IConfiguration configuration)
        {
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
            _scan = scan;

            // store appsettings.json path next to assembly
            var basePath = AppContext.BaseDirectory;
            _configPath = Path.Combine(basePath, "appsettings.json");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker starting at: {StartTime}", DateTimeOffset.Now);  
            try
            {
                _logger.LogInformation("Registering the agent for agentId at {StartTime}", DateTimeOffset.Now);
                var registrationResponse = await RegisterAgent(stoppingToken);
                if (registrationResponse != null)
                {
                   
                    _pollingIntervalMinutes = 5;
                    // start heartbeat loop in parallel
                    _ = Task.Run(() => HeartbeatLoop(registrationResponse.AgentId, stoppingToken), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent registration failed");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task<AgentRegistrationResponse?> RegisterAgent(CancellationToken cancellationToken)
        {
            var request = new AgentRegistrationRequest
            {
                MachineName = Environment.MachineName,
                CurrentUser = Environment.UserName,
                MacAddress = GetMacAddress() ?? string.Empty,
                OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                OsVersion = Environment.OSVersion.VersionString,
                AgentVersion = "1.0.0"
            };

            // Replace with real registration endpoint
            var registrationUrl = _configuration["AgentRegistration:Url"] ?? "https://example.com/api/register";

            _logger.LogInformation("Registering agent at {url} with machine {machine}", registrationUrl, request.MachineName);

            var response = await _httpClient.PostAsJsonAsync(registrationUrl, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Registration failed with status code {code}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadFromJsonAsync<AgentRegistrationResponse>(cancellationToken: cancellationToken);
            if (content == null || string.IsNullOrWhiteSpace(content.AgentId))
            {
                _logger.LogWarning("Registration response invalid");
                return null;
            }

            _logger.LogInformation("Registered agent id {agentId}, polling interval {minutes} minutes", content.AgentId, _pollingIntervalMinutes);

            await UpdateLocalConfigAgentId(content.AgentId);
            return content;
        }

        private async Task HeartbeatLoop(string agentId, CancellationToken cancellationToken)
        {
            try
            {
                
                var heartbeatUrl = _configuration["AgentRegistration:HeartbeatUrl"];
                var registrationUrl = _configuration["AgentRegistration:Url"] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(heartbeatUrl) && !string.IsNullOrWhiteSpace(registrationUrl))
                {
                    // derive heartbeat url from registration url if explicit heartbeat url not provided
                    heartbeatUrl = registrationUrl.EndsWith("/register", StringComparison.OrdinalIgnoreCase)
                        ? registrationUrl.Substring(0, registrationUrl.Length - "/register".Length) + "/heartbeat"
                        : registrationUrl.TrimEnd('/') + "/heartbeat";
                }

                if (string.IsNullOrWhiteSpace(heartbeatUrl))
                {
                    _logger.LogWarning("No heartbeat URL configured; skipping heartbeat loop");
                    return;
                }

                _logger.LogInformation("Starting heartbeat loop to {url} every {minutes} minutes", heartbeatUrl, _pollingIntervalMinutes);

                while (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Sending Heartbeat for agent id: {agentId}, polling interval {minutes} minutes", agentId, _pollingIntervalMinutes);
                    var heartbeat = new AgentHeartbeatRequest
                    {
                        AgentId = agentId,
                        MachineName = Environment.MachineName,
                        TimestampUtc = DateTimeOffset.UtcNow,
                        Status = "alive"
                    };

                    try
                    {
                        var resp = await _httpClient.PostAsJsonAsync(heartbeatUrl, heartbeat, cancellationToken);
                        if (!resp.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("Heartbeat failed with status {status}", resp.StatusCode);
                        }
                        else
                        {
                            _logger.LogDebug("Heartbeat sent at {time}", heartbeat.TimestampUtc);

                            // read potential scan config in response
                            try
                            {
                                var hbResponse = await resp.Content.ReadFromJsonAsync<HeartbeatResponse>(cancellationToken: cancellationToken);
                                if (hbResponse != null && !string.IsNullOrWhiteSpace(hbResponse.ScanConfig?.Id))
                                {
                                    _logger.LogInformation("Received scan config {scanId} - {scanName}", hbResponse.ScanConfig.Id, hbResponse.ScanConfig.Name);
                                    _ = Task.Run(() => _scan.RunAsync(hbResponse.ScanConfig, CancellationToken.None),CancellationToken.None);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse heartbeat response content");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Exception sending heartbeat");
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(_pollingIntervalMinutes), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // graceful exit
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat loop failed to start");
            }
        }

        

        private async Task UpdateLocalConfigAgentId(string agentId)
        {
            try
            {
                // load existing config file if exists, otherwise create basic structure
                var json = File.Exists(_configPath) ? await File.ReadAllTextAsync(_configPath) : "{}";
                JsonNode? root = null;
                try
                {
                    root = JsonNode.Parse(json) ?? new JsonObject();
                }
                catch
                {
                    root = new JsonObject();
                }

                if (root is JsonObject obj)
                {
                    if (!obj.TryGetPropertyValue("AgentSettings", out var agentNode) || agentNode is null)
                    {
                        obj["AgentSettings"] = new JsonObject { ["AgentId"] = agentId };
                    }
                    else if (agentNode is JsonObject agentObj)
                    {
                        agentObj["AgentId"] = agentId;
                    }
                    else
                    {
                        obj["AgentSettings"] = new JsonObject { ["AgentId"] = agentId };
                    }
                }
                else
                {
                    // unexpected root type, replace
                    root = new JsonObject { ["AgentSettings"] = new JsonObject { ["AgentId"] = agentId } };
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var updated = root.ToJsonString(options);
                await File.WriteAllTextAsync(_configPath, updated);

                _logger.LogInformation("Updated local config with AgentId");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update local config");
            }
        }
        
        private string? GetMacAddress()
        {
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && nic.GetPhysicalAddress()?.GetAddressBytes().Length >0);

                var mac = interfaces.FirstOrDefault()?.GetPhysicalAddress()?.ToString();
                if (string.IsNullOrWhiteSpace(mac)) return null;

                // format as00-11-22-33-44-55
                return string.Join('-', Enumerable.Range(0, mac.Length /2).Select(i => mac.Substring(i *2,2)));
            }
            catch
            {
                return null;
            }
        }
    }

    internal class AgentRegistrationRequest
    {
        public string MachineName { get; set; } = default!;
        public string CurrentUser { get; set; } = default!;
        public string MacAddress { get; set; } = default!;
        public string OperatingSystem { get; set; } = default!;
        public string OsVersion { get; set; } = default!;
        public string AgentVersion { get; set; } = default!;
    }

    internal class AgentRegistrationResponse
    {
        public string AgentId { get; set; } = default!;
      //  public int PollingIntervalMinutes { get; set; } = 5;
    }

    internal class AgentHeartbeatRequest
    {
        public string AgentId { get; set; } = default!;
        public string MachineName { get; set; } = default!;
        public DateTimeOffset TimestampUtc { get; set; }
        public string Status { get; set; } = default!;
    }   
}
