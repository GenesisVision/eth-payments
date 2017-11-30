using Nethereum.Hex.HexTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace EthPayments.Models
{
    class StructLogs
    {
        public int Pc { get; set; }
        public string Op { get; set; }
        public int Gas { get; set; }
        public int GasCost { get; set; }
        public int Depth { get; set; }
        public string Error { get; set; }
        public List<string> Stack { get; set; }
        public List<string> Memory { get; set; }
        public object Storage { get; set; }
    }
}
