using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fingerprint_bridge.Models
{
    public class FingerprintTemplate
    {
        public int UserId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public byte[] Template { get; set; }
    }
}
