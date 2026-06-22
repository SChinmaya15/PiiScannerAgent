using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PiiScanner.Data;
using PiiScanner.Models;

namespace PiiScanner
{
    public sealed class ResultUploader : BackgroundService
    {
        private readonly ILogger<ResultUploader> _logger;
        private readonly ScanResultRepository _repo;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _emptyDelay = TimeSpan.FromSeconds(5);

        public ResultUploader(
            ILogger<ResultUploader> logger,
            ScanResultRepository repo,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _repo = repo;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ResultUploader started.");

            var endpoint = _configuration["AgentRegistration:ScanResultUrl"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogError("No AgentRegistration:ScanResultUrl configured. Exiting uploader.");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var batch = await _repo.DequeueBatchAsync(100);
                    if (batch == null || batch.Count == 0)
                    {
                        await Task.Delay(_emptyDelay, stoppingToken);
                        continue;
                    }

                    var payload = JsonSerializer.Serialize(batch);
                    var content = new StringContent(payload, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync(endpoint, content, stoppingToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var ids = batch.Select(b => b.Id);
                        await _repo.DeleteBatchAsync(ids);
                        _logger.LogInformation("Uploaded and deleted {Count} scan results.", batch.Count);
                    }
                    else
                    {
                        _logger.LogWarning("Upload failed with status code {StatusCode}. Will retry later.", response.StatusCode);
                        // wait before retrying to avoid tight error loop
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("ResultUploader cancellation requested.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in ResultUploader loop. Waiting before retry.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            _logger.LogInformation("ResultUploader stopping.");
        }
    }
}