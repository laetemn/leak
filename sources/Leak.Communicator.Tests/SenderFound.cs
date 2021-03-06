﻿using Leak.Networking.Core;
using Leak.Peer.Sender.Core;

namespace Leak.Peer.Sender.Tests
{
    public class SenderFound : SenderMessage, NetworkOutgoingMessage
    {
        public string Type
        {
            get { return "found"; }
        }

        public NetworkOutgoingMessage Apply(byte id)
        {
            return this;
        }

        public int Length
        {
            get { return 5; }
        }

        public void ToBytes(DataBlock block)
        {
        }

        public void Release()
        {
        }
    }
}