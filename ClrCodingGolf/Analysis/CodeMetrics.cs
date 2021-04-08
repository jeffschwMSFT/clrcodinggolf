using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ClrCodingGolf.Analysis
{
    public class CodeMetrics
    {
        public string Text { get; set; }
        public float CCM { get; set; }
        public int Methods { get; set; }
        public int Bytes { get; set; }
        public int Lines { get; set; }
        public float Characters { get; set; }
        public long Duration { get; set; }
    }
}
