using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Models
{
    public class HeartbeatResponse
    {
        public ScanConfig? ScanConfig { get; set; }
        public string AgentId { get; set; } = default!;
        public int PollingIntervalMinutes { get; set; }
    }
}
