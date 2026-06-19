using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Models
{
    public class ScanResults
    {
        public string ScanId { get; set; }  
        public Guid Id { get; set; }
        public string MachineName { get; set; }
        public StorageSource Source { get; set; }
        public string FilePath { get; set; }
        public string Entity { get; set; }
        public bool IsDetected { get; set; }
        public string Details { get; set; }

    }
}
