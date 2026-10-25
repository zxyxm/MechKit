using System;
using System.Collections.Generic;
using System.IO;

namespace MechKit.UI
{
    internal sealed class ModelPreviewCache
    {
        private readonly Dictionary<string, byte[]> _entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Queue<string> _order = new Queue<string>();
        private readonly int _limit;
        private long _bytes;
        private const long ByteLimit = 16 * 1024 * 1024;
        internal ModelPreviewCache(int limit = 32) { _limit = Math.Max(1, limit); }
        internal byte[] Read(string path, string configuration, Func<byte[]> read)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            var key=file.FullName.ToUpperInvariant()+"\0"+(configuration??"")+"\0"+MechKit.Core.BomAssets.Stamp(path);
            byte[] value;
            if(_entries.TryGetValue(key,out value)) return value;
            using (MechKit.Core.OperationTrace.Begin("SW model preview file=" + path + " config=" + configuration))
                value=read();
            var size=value==null?0:value.Length;
            if(size>ByteLimit) return value;
            while(_order.Count>0 && (_entries.Count>=_limit || _bytes+size>ByteLimit))
            {
                var old=_order.Dequeue();var bytes=_entries[old];
                _bytes-=bytes==null?0:bytes.Length;_entries.Remove(old);
            }
            _entries[key]=value;_order.Enqueue(key);_bytes+=size;
            return value;
        }
        internal void Clear() { _entries.Clear();_order.Clear();_bytes=0; }
    }
}
