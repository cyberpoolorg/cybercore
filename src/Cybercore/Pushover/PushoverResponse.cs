using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cybercore.Pushover
{
    public class PushoverReponse
    {
        public int Status { get; set; }
        public string Request { get; set; }
        public string User { get; set; }
        public string[] Errors { get; set; }
    }
}