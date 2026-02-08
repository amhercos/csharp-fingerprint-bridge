using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fingerprint_bridge.Models
{
    public class AppSettings
    {
        public string ServerIp { get; set; } = "localhost";
        public int DatabasePort { get; set; } = 5432;
        public string DatabaseUser { get; set; } = "postgres";
        public string DatabasePass { get; set; } = "password1";
        public string DatabaseName { get; set; } = "fingerprint";
        public int TerminalId { get; set; } = 1;
        public int SyncIntervalMinutes { get; set; } = 5;
    }
}
