using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fingerprint_bridge.Models
{
    public class AttendanceRequest
    {
        public int FingerprintId { get; set; }
        public int TerminalId { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
