﻿namespace Leak.Core.Retriever
{
    public class ResourceStorageConfiguration
    {
        public int Pieces { get; set; }

        public int PieceSize { get; set; }

        public int BlockSize { get; set; }

        public long TotalSize { get; set; }
    }
}