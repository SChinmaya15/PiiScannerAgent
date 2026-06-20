using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Models
{
    public class ScanConfig
    {
        public string? Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public List<Agents> AgentIds { get; set; }
        public string ScanType { get; set; }
        public ScanSource Source { get; set; }
        public ScanFilters Filters { get; set; }
        public ScanSchedule Schedule { get; set; }
        public ScanActions Actions { get; set; }
        public DetectionConfig Detection { get; set; }
        public CloudCredentials? CloudCredentials { get; set; }
        public ExecutionConfig Execution { get; set; }
        public DateTime? LastRun { get; set; } = null;
    }

    public class ScanSource
    {
        public int Location { get; set; }//
        public string Path { get; set; }
        public string ScanMode { get; set; }
        public Credentials? Credentials { get; set; }
    }
    public class Agents
    {
        public string AgentId { get; set; }
        public string Status { get; set; }
    }
    public class Credentials
    {
        public string Username { get; set; }
        public string PasswordEncrypted { get; set; }
    }

    public class ScanFilters
    {
        public List<string> Extensions { get; set; }
        public bool IncludeSubDirectories { get; set; }
        public int MaxFileSizeMB { get; set; }
    }

    public class ScanSchedule
    {
        public string Frequency { get; set; }
        public DateTime? NextRun { get; set; }
    }

    public class ScanActions
    {
        public string Type { get; set; }
        public string QuarantinePath { get; set; }
        public bool RemediationEnabled { get; set; }
    }

    public class DetectionConfig
    {
        public bool ScanForPII { get; set; }
        public List<string> Entities { get; set; }
    }

    public class CloudCredentials
    {
        public string ApiKey { get; set; }
        public string SecretKey { get; set; }
    }

    public class ExecutionConfig
    {
        public bool OverwriteExistingResults { get; set; }
        public bool StopPreviousScan { get; set; }
        public int ParallelThreads { get; set; }
        public int RetryCount { get; set; }
        public string LogLevel { get; set; }
    }

    // ─── Detection Result ──────────────────────────────────────────────────────────

    public class DetectionResult
    {
        public string FilePath { get; set; }
        public string Entity { get; set; }
        public bool IsDetected { get; set; }
        public string Details { get; set; }
    }

}
