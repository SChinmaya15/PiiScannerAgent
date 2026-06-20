using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Models
{
    public enum StorageSource
    {
        LOCAL=0,
        AWS_S3=1,
        DROPBOX=2,
        ONEDRIVE=3,
        GOOGLE_DRIVE=4
    }
}
