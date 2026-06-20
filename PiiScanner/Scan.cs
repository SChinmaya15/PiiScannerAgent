using PiiScanner.Models;
using PiiScanner.Validators;
using System.Text;
using System.Text.Json;

namespace PiiScanner
{
    public class Scan
    {
        private readonly ILogger<Scan> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly string _configPath;
        private int _pollingIntervalMinutes = 5;
        private const int MaxConcurrentThreads = 10;     // hard cap
        private ScanConfig _config;
        private readonly SemaphoreSlim _semaphore;
        private string machineName;



        public Scan(ILogger<Scan> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Honour config's parallelThreads but never exceed the hard cap of 10
            int concurrency = Math.Min(
                MaxConcurrentThreads,
                MaxConcurrentThreads);

            _semaphore = new SemaphoreSlim(concurrency, concurrency);
        }

        public async Task RunAsync(ScanConfig config, CancellationToken cancellationToken = default)
        {
            _config = config;
            // 1. Resolve scan root and extension whitelist from JSON config
            string scanPath = ResolveScanPath();
            HashSet<string> allowedExtensions = ResolveExtensions();
            long maxBytes = (_config.Filters?.MaxFileSizeMB ?? 100) * 1024L * 1024L;

            _logger.LogInformation($"Scan root   : {scanPath}");
            _logger.LogInformation($"Extensions  : {string.Join(", ", allowedExtensions)}");
            _logger.LogInformation($"Max size    : {_config.Filters?.MaxFileSizeMB ?? 100} MB");

            // 2. Discover files that match the filters
            IEnumerable<string> files = DiscoverFiles(scanPath,
                                                      allowedExtensions,
                                                      maxBytes);

            // 3. Fan-out: each file gets its own Task, bounded by the semaphore
            var tasks = new List<Task>();

            foreach (string filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Capture loop variable for the closure
                string capturedPath = filePath;

                Task t = Task.Run(async () =>
                {
                    // Block here until a slot is free (queue semantics)
                    await _semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await ProcessFileWithRetryAsync(capturedPath, cancellationToken);
                    }
                    finally
                    {
                        // Always release so the next queued task can proceed
                        _semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(t);
            }
            // 4. Wait for every file to finish (or fault)
            await Task.WhenAll(tasks);

            _logger.LogInformation("Scan complete.");
        }

        // ── Step 1 – resolve path ─────────────────────────────────────────────────

        private string ResolveScanPath()
        {
            string path = _config.Source?.Path;

            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("source.path is missing in the scan config.");

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"Scan path does not exist: {path}");

            return path;
        }

        // ── Step 2 – resolve extension whitelist ──────────────────────────────────

        private HashSet<string> ResolveExtensions()
        {
            var raw = _config.Filters?.Extensions;

            if (raw == null || raw.Count == 0)
                throw new InvalidOperationException("filters.extensions is empty in the scan config.");

            // Normalise: lowercase, ensure leading dot
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string ext in raw)
            {
                string normalised = ext.StartsWith(".") ? ext.ToLowerInvariant()
                                                        : "." + ext.ToLowerInvariant();
                set.Add(normalised);
            }
            return set;
        }


        // ── Step 3 – discover matching files ─────────────────────────────────────

        private IEnumerable<string> DiscoverFiles(
            string root,
            HashSet<string> allowedExtensions,
            long maxBytes)
        {
            var searchOption = _config.Filters?.IncludeSubDirectories == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            foreach (string file in Directory.EnumerateFiles(root, "*", searchOption))
            {
                string ext = Path.GetExtension(file);

                if (!allowedExtensions.Contains(ext))
                    continue;

                var info = new FileInfo(file);
                if (info.Length > maxBytes)
                {
                    Log($"[SKIP] Exceeds size limit: {file} ({info.Length / (1024 * 1024)} MB)");
                    continue;
                }

                yield return file;
            }
        }
        // ── Step 4 – process one file (with retry) ────────────────────────────────

        private async Task ProcessFileWithRetryAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            int maxRetries = _config.Execution?.RetryCount ?? 3;

            for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
            {
                try
                {
                    Log($"[Thread {Thread.CurrentThread.ManagedThreadId}] Processing: {filePath} (attempt {attempt})");

                    // Read file content
                    string content = await ReadFileContentAsync(filePath, cancellationToken);

                    // Call the 3rd-party abstract detect method
                    List<ScanResults> results = await DetectAsync(
                        filePath,
                        content,
                        _config.Detection?.Entities ?? new List<string>());

                    HandleResults(filePath, results);
                    return; // success — exit retry loop
                }
                catch (OperationCanceledException)
                {
                    throw; // do not swallow cancellation
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] {filePath} – attempt {attempt} failed: {ex.Message}");

                    if (attempt > maxRetries)
                        Log($"[FAILED] Giving up on: {filePath}");
                    else
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken); // exponential back-off
                }
            }
        }



        // ── Read file bytes → string ──────────────────────────────────────────────

        private static async Task<string> ReadFileContentAsync(
            string filePath,
            CancellationToken cancellationToken)
        {
            // For binary formats (PDF, DOCX) you would invoke a parser here.
            // Raw UTF-8 read is shown as the default; swap in your parser per extension.
            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return Encoding.UTF8.GetString(bytes);
        }

        // ── Handle detection results ──────────────────────────────────────────────

        private async void HandleResults(string filePath, List<ScanResults> results)
        {
            if (results == null || results.Count == 0)
            {
                _logger.LogInformation($"[CLEAN] No PII found: {filePath}");
                return;
            }
            // TODO: persist results to database, blob, or other storage as needed and send it to Kafka
            foreach (var r in results)
            {
                if (r.IsDetected)
                    _logger.LogInformation($"[HIT]   {r.Entity} detected in {filePath} – {r.Details}");
            }
            // Post scan results to worker endpoint
            try
            {
                var json = JsonSerializer.Serialize(results);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpClient = new HttpClient();
                var response = await httpClient.PostAsync(
                    "https://localhost:44346/worker/scanresult", content);

                response.EnsureSuccessStatusCode();

                _logger.LogInformation($"[POST] Scan results successfully sent for: {filePath}");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"[POST] Failed to send scan results for: {filePath}");
            }


            // Hook: notify / quarantine / remediate based on Actions config
            if (_config.Actions?.Type == "NotifyOnly")
                Notify(filePath, results);
        }

        private void Notify(string filePath, List<ScanResults> results)
        {
            // Plug in your notification logic (email, webhook, etc.)
            Log($"[NOTIFY] PII found in {filePath} – entities: {string.Join(", ", results.ConvertAll(r => r.Entity))}");
        }

        private static void Log(string message) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");



        public Task<List<ScanResults>> DetectAsync(
            string filePath,
           string content,
           List<string> entities)
        {
            var results = new List<ScanResults>();
            var pans = PanValidator.ExtractPANs(content);
            if (pans.Any())//Regex.IsMatch(content, @"\b[A-Z]{5}[0-9]{4}[A-Z]\b"))
                results.Add(new ScanResults
                {
                    ScanId = _config.Id,
                    Id = Guid.NewGuid(),
                    MachineName = Environment.MachineName,
                    Source = StorageSource.LOCAL,
                    FilePath = filePath,
                    Entity = "PAN",
                    IsDetected = true,
                    Details = $"Found PAN: {string.Join(", ", pans)}"
                });

            var aadhaars = AadhaarValidator.ExtractAadhaarNumbers(content);
            if (aadhaars.Any())
                results.Add(new ScanResults
                {
                    ScanId = _config.Id,
                    Id = Guid.NewGuid(),
                    MachineName = Environment.MachineName,
                    Source = StorageSource.LOCAL,
                    FilePath = filePath,
                    Entity = "Aadhaar",
                    IsDetected = true,
                    Details = $"Found Aadhaar: {string.Join(", ", aadhaars)}"
                });
            var phones = MobileNumberValidator.ExtractPhoneNumbers(content);
            if (phones.Any())
                results.Add(new ScanResults
                {
                    ScanId = _config.Id,
                    Id = Guid.NewGuid(),
                    MachineName = Environment.MachineName,
                    Source = StorageSource.LOCAL,
                    FilePath = filePath,
                    Entity = "Phone",
                    IsDetected = true,
                    Details = $"Found Phone: {string.Join(", ", phones)}"
                });
            var emails = EmailValidator.ExtractEmails(content);
            if (emails.Any())
                results.Add(new ScanResults
                {
                    ScanId = _config.Id,
                    Id = Guid.NewGuid(),
                    MachineName = Environment.MachineName,
                    Source = StorageSource.LOCAL,
                    FilePath = filePath,
                    Entity = "Email",
                    IsDetected = true,
                    Details = $"Found Email: {string.Join(", ", emails)}"
                });
            var creditCard = CreditCardValidator.ExtractCreditCards(content);
            if (creditCard.Any())
                results.Add(new ScanResults
                {
                    ScanId = _config.Id,
                    Id = Guid.NewGuid(),
                    MachineName = Environment.MachineName,
                    Source = StorageSource.LOCAL,
                    FilePath = filePath,
                    Entity = "CreditCard",
                    IsDetected = true,
                    Details = $"Found CreditCard: {string.Join(", ", creditCard)}"
                });
            var bankAccounts = BankAccountValidator.ExtractBankAccounts(content);
            if (bankAccounts.Any())
                results.Add(new ScanResults
                {
                    ScanId = _config.Id,
                    Id = Guid.NewGuid(),
                    MachineName = Environment.MachineName,
                    Source = StorageSource.LOCAL,
                    FilePath = filePath,
                    Entity = "BankAccount",
                    IsDetected = true,
                    Details = $"Found BankAccount: {string.Join(", ", bankAccounts)}"
                });

            return Task.FromResult(results);
        }
    }
}
