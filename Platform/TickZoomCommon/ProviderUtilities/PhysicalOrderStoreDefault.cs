using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.Common
{
    public class PhysicalOrderStoreDefault : PhysicalOrderCacheDefault, PhysicalOrderStore
    {
        private Log log;
        private volatile bool info;
        private volatile bool trace;
        private volatile bool debug;
        private volatile bool anySnapShotWritten = false;
        public override void RefreshLogLevel()
        {
            if (log != null)
            {
                info = log.IsDebugEnabled;
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }

        private string databasePath;
        private FileStream fs;
        private MemoryStream memory = null;
        private BinaryWriter writer = null;
        private BinaryReader reader = null;
        private Dictionary<CreateOrChangeOrder, int> unique = new Dictionary<CreateOrChangeOrder, int>();
        private Dictionary<int,CreateOrChangeOrder> uniqueIds = new Dictionary<int,CreateOrChangeOrder>();
        private Dictionary<int,int> replaceIds = new Dictionary<int,int>();
        private Dictionary<int, int> originalIds = new Dictionary<int, int>();
        private TimeStamp lastSequenceReset;
        private int uniqueId = 0;
        private Action writeFileAction;
        private IAsyncResult writeFileResult;
        private long snapshotLength = 0;
        private int updateCount = 0;
        private long snapshotRolloverSize = 128*1024;
        private string storeName;
        private string dbFolder;
        private int remoteSequence = 0;
        private int localSequence = 0;
        private object fileLocker = new object();
        private object snapshotLocker = new object();
        private bool snapshotNeeded;
        private PhysicalOrderLock physicalOrderLock;

        public PhysicalOrderStoreDefault(string name)
        {
            log = Factory.SysLog.GetLogger(typeof(PhysicalOrderStoreDefault));
            log.Register(this);
            storeName = name;
            writeFileAction = SnapShotHandler;
            var appData = Factory.Settings["AppDataFolder"];
            dbFolder = Path.Combine(appData, "DataBase");
            Directory.CreateDirectory(dbFolder);
            databasePath = Path.Combine(dbFolder, name + ".dat");
            memory = new MemoryStream();
            writer = new BinaryWriter(memory, Encoding.UTF8);
            reader = new BinaryReader(memory, Encoding.UTF8);
            physicalOrderLock = new PhysicalOrderLock(this);
        }

        public class PhysicalOrderLock : IDisposable
        {
            private PhysicalOrderStore lockedCache;
            internal PhysicalOrderLock(PhysicalOrderStore cache)
            {
                lockedCache = cache;
            }
            public void Dispose()
            {
                lockedCache.EndTransaction();
            }
        }

        public IDisposable BeginTransaction()
        {
            return physicalOrderLock;
        }

        public void EndTransaction()
        {
        }

        public bool IsLocked
        {
            get { return false; }
        }

        public void AssertAtomic()
        {
            //if (!IsLocked)
            //{
            //    var message = "Attempt to modify PhysicalOrder w/o locking PhysicalOrderStore first.";
            //    log.Error(message + "\n" + Environment.StackTrace);
            //    //throw new ApplicationException(message);
            //}
        }

        public bool TryOpen()
        {
            if( fs != null && fs.CanWrite) return true;
            var list = new List<Exception>();
            var errorCount = 0;
            while( errorCount < 3)
            {
                try
                {
                    fs = new FileStream(databasePath, FileMode.Append, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough);
                    if( debug) log.Debug("Open " + storeName);
                    snapshotLength = fs.Length;
                    return true;
                }
                catch (IOException ex)
                {
                    list.Add(ex);
                    Thread.Sleep(1000);
                    errorCount++;
                }
            }
            if( list.Count > 0)
            {
                var ex = list[list.Count - 1];
                throw new ApplicationException( "Failed to open the snapshot file after 3 tries", ex);
            }
            return false;
        }

        public void TrySnapshot()
        {
            if (snapshotNeeded)
            {
                StartSnapShot();
                snapshotNeeded = false;
            }
            else
            {
                TryStartSnapshot();
            }
        }

        public void IncrementUpdateCount()
        {
            ++updateCount;
        }

        public string DatabasePath
        {
            get { return databasePath; }
        }

        public long SnapshotRolloverSize
        {
            get { return snapshotRolloverSize; }
            set { snapshotRolloverSize = value; }
        }

        public int RemoteSequence
        {
            get { return remoteSequence; }
        }

        public int LocalSequence
        {
            get { return localSequence; }
        }

        public TimeStamp LastSequenceReset
        {
            get { return lastSequenceReset; }
            set { lastSequenceReset = value; }
        }

        private bool AddUniqueOrder(CreateOrChangeOrder order)
        {
            AssertAtomic();
            int id;
            if( !unique.TryGetValue(order, out id))
            {
                unique.Add(order,++uniqueId);
                return true;
            }
            return false;
        }

        private void TryStartSnapshot()
        {
            if (updateCount > 100)
            {
                StartSnapShot();
            }
        }

        public void RequestSnapshot()
        {
            snapshotNeeded = true;
        }

        private void StartSnapShot()
        {
            SnapShotInMemory();
            lock( snapshotLocker)
            {
                if(writeFileResult != null)
                {
                    if (writeFileResult.IsCompleted)
                    {
                        writeFileAction.EndInvoke(writeFileResult);
                        writeFileResult = null;
                    }
                    else
                    {
                        if( debug) log.Debug("StartSnapshot() Snapshot already in progress.");
                        return;
                    }
                }
                if( writeFileResult == null)
                {
                    writeFileResult = writeFileAction.BeginInvoke(null, null);
                    updateCount = 0;
                }
            }
        }

        public bool IsBusy
        {
            get
            {
                return writeFileResult != null && !writeFileResult.IsCompleted;
            }
        }

        public void WaitForSnapshot()
        {

            while (IsBusy)
            {
                Thread.Sleep(100);
            }
            lock(snapshotLocker)
            {
                if (writeFileResult != null && writeFileResult.IsCompleted)
                {
                    writeFileAction.EndInvoke(writeFileResult);
                    writeFileResult = null;
                }
            }
        }

        public struct SnapshotFile
        {
            public int Order;
            public string Filename;
        }

        private IList<SnapshotFile> FindSnapshotFiles()
        {
            var files = Directory.GetFiles(dbFolder, storeName + ".dat.*", SearchOption.TopDirectoryOnly);
            var fileList = new List<SnapshotFile>();
            foreach (var file in files)
            {
                var parts = file.Split('.');
                if (parts.Length == 3)
                {
                    int count;
                    if (int.TryParse(parts[2], out count) && count > 0)
                    {
                        fileList.Add(new SnapshotFile { Order = count, Filename = file });
                    }
                }
                else
                {
                    fileList.Add(new SnapshotFile { Order = 0, Filename = file });
                }
            }
            fileList.Sort((a,b) => a.Order - b.Order);
            return fileList;
        }

        private void ForceSnapshotRollover()
        {
            if( debug) log.Debug("Creating new snapshot file and rolling older ones to higher number.");
            lock( fileLocker)
            {
                TryClose();
                var files = FindSnapshotFiles();
                for (var i = files.Count - 1; i >= 0; i--)
                {
                    var count = files[i].Order;
                    var source = files[i].Filename;
                    if (File.Exists(source))
                    {
                        if (count > 9)
                        {
                            File.Delete(source);
                        }
                        else
                        {
                            var replace = Path.Combine(dbFolder, storeName + ".dat." + (count + 1));
                            var errorCount = 0;
                            var errorList = new List<Exception>();
                            while (errorCount < 3)
                            {
                                try
                                {
                                    File.Move(source, replace);
                                    break;
                                }
                                catch (IOException ex)
                                {
                                    errorList.Add(ex);
                                    errorCount++;
                                    Thread.Sleep(1000);
                                }
                            }
                            if (errorList.Count > 0)
                            {
                                var ex = errorList[errorList.Count - 1];
                                throw new ApplicationException("Failed to mov " + source + " to " + replace, ex);
                            }
                        }
                    }
                }
            }
        }

        private void CheckSnapshotRollover()
        {
            if (snapshotLength >= SnapshotRolloverSize || forceSnapShotRollover)
            {
                forceSnapShotRollover = false;
                log.Info("Snapshot length greater than snapshot rollover: " + SnapshotRolloverSize);
                ForceSnapshotRollover();
            }
        }

        private IEnumerable<CreateOrChangeOrder> OrderReferences(CreateOrChangeOrder order)
        {
            if( order.ReplacedBy != null)
            {
                if( AddUniqueOrder(order.ReplacedBy))
                {
                    yield return order.ReplacedBy;
                    foreach (var sub in OrderReferences(order.ReplacedBy))
                    {
                        if (AddUniqueOrder(sub))
                        {
                            yield return sub;
                        }
                    }
                }
            }
            if( order.OriginalOrder != null)
            {
                if( AddUniqueOrder(order.OriginalOrder))
                {
                    yield return order.OriginalOrder;
                    foreach (var sub in OrderReferences(order.OriginalOrder))
                    {
                        if (AddUniqueOrder(sub))
                        {
                            yield return sub;
                        }
                    }
                }
            }
        }

        private void SnapShotHandler()
        {
            try
            {
                if( debug) log.Debug("SnapshotHandler()");
                SnapShotFlushToDisk();
            }
            catch (Exception ex)
            {
                log.Error(ex.Message, ex);
            }
        }

        private void SnapShotInMemory()
        {
            anySnapShotWritten = true;
            CheckSnapshotRollover();

            memory.SetLength(0);
            uniqueId = 0;
            unique.Clear();

            // Save space for length.
            writer.Write((int) memory.Length);
            // Write the current sequence number
            writer.Write(remoteSequence);
            writer.Write(LocalSequence);
            writer.Write(lastSequenceReset.Internal);
            if (debug)
                log.Debug("Snapshot writing Local Sequence  " + localSequence + ", Remote Sequence " +
                          remoteSequence);
            foreach (var kvp in ordersByBrokerId)
            {
                var order = kvp.Value;
                AddUniqueOrder(order);
                if (trace) log.Trace("Snapshot found order by Id: " + order);
                foreach (var reference in OrderReferences(order))
                {
                    AddUniqueOrder(reference);
                }
            }

            foreach (var kvp in ordersBySerial)
            {
                var order = kvp.Value;
                AddUniqueOrder(order);
                if (trace) log.Trace("Snapshot found order by serial: " + order);
                foreach (var reference in OrderReferences(order))
                {
                    AddUniqueOrder(reference);
                }
            }

            writer.Write(unique.Count);
            foreach (var kvp in unique)
            {
                var order = kvp.Key;
                if (trace) log.Trace("Snapshot writing unique order: " + order);
                var id = kvp.Value;
                writer.Write(id);
                writer.Write((int) order.Action);
                writer.Write(order.BrokerOrder);
                writer.Write(order.LogicalOrderId);
                writer.Write(order.LogicalSerialNumber);
                writer.Write((int) order.OrderState);
                writer.Write(order.Price);
                writer.Write((int) order.OrderFlags);
                if (order.ReplacedBy != null)
                {
                    try
                    {
                        writer.Write(unique[order.ReplacedBy]);
                    }
                    catch (KeyNotFoundException ex)
                    {
                        var sb = new StringBuilder();
                        foreach (var kvp2 in unique)
                        {
                            var temp = kvp2.Value;
                            var temp2 = kvp2.Key;
                            sb.AppendLine(temp.ToString() + ": " + temp2.ToString());
                        }
                        throw new ApplicationException("Can't find " + order.ReplacedBy + "\n" + sb, ex);
                    }
                }
                else
                {
                    writer.Write((int) 0);
                }
                if (order.Action == OrderAction.Cancel && order.OriginalOrder == null)
                {
                    throw new ApplicationException("Cancel order w/o any original order setting: " + order);
                }
                if (order.Action == OrderAction.Change && order.OriginalOrder == null)
                {
                    throw new ApplicationException("Change order w/o any original order setting: " + order);
                }
                if (order.OriginalOrder != null)
                {
                    try
                    {
                        writer.Write(unique[order.OriginalOrder]);
                    }
                    catch (KeyNotFoundException ex)
                    {
                        throw new ApplicationException("Can't find " + order.ReplacedBy, ex);
                    }
                }
                else
                {
                    writer.Write((int) 0);
                }
                writer.Write((int) order.Side);
                writer.Write((int) order.Size);
                writer.Write(order.Symbol.Symbol);
                if (order.Tag == null)
                {
                    writer.Write("");
                }
                else
                {
                    writer.Write(order.Tag);
                }
                writer.Write((int) order.Type);
                writer.Write(order.UtcCreateTime.Internal);
                writer.Write(order.LastModifyTime.Internal);
                writer.Write(order.Sequence);
            }

            writer.Write(ordersBySerial.Count);
            foreach (var kvp in ordersBySerial)
            {
                var serial = kvp.Key;
                var order = kvp.Value;
                writer.Write(serial);
                writer.Write(unique[order]);
            }

            using (positionsLocker.Using())
            {
                if (debug) log.Debug("Symbol Positions\n" + SymbolPositionsToStringInternal());

                var positionCount = 0;
                foreach (var kvp in positions)
                {
                    var symbolPosition = kvp.Value;
                    if (symbolPosition.Position != 0)
                    {
                        positionCount++;
                    }
                }
                writer.Write(positionCount);


                foreach (var kvp in positions)
                {
                    var symbolPosition = kvp.Value;
                    if (symbolPosition.Position != 0)
                    {
                        positionCount++;
                        var symbol = Factory.Symbol.LookupSymbol(kvp.Key);
                        writer.Write(symbol.Symbol);
                        writer.Write(kvp.Value.Position);
                    }
                }

            }
        }

        private void SnapShotFlushToDisk() {

            memory.Position = 0;
            writer.Write((Int32)memory.Length - sizeof(Int32)); // length excludes the size of the length value.
            if( TryOpen())
            {
                fs.Write(memory.GetBuffer(), 0, (int)memory.Length);
                snapshotLength += memory.Length;
                log.Info("Wrote snapshot. Sequence Remote = " + remoteSequence + ", Local = " + localSequence +
                         ", Size = " + memory.Length + ". File Size = " + snapshotLength);
            }
            if( isDisposed)
            {
                TryClose();
            }
        }

        private void SnapshotReadAll(string filePath)
        {
            using (var readFS = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var count = 0;
                memory.SetLength(snapshotRolloverSize<<2);
                memory.Position = 0;
                do
                {
                    count = readFS.Read(memory.GetBuffer(), (int)memory.Position, (int)(memory.Length - count));
                    memory.Position += count;
                } while (count > 0);
                memory.SetLength(memory.Position);
            }
        }

        public struct Snapshot
        {
            public int Offset;
            public int Length;
        }

        private IList<Snapshot> SnapshotScan()
        {
            var snapshots = new List<Snapshot>();
            memory.Position = 0;
            while (memory.Position < memory.Length)
            {
                var snapshot = new Snapshot {Offset = (int) memory.Position, Length = reader.ReadInt32()};
                if (snapshot.Length <= 0 || memory.Position + snapshot.Length > memory.Length)
                {
                    log.Warn("Invalid snapshot length: " + snapshot.Length + ". Probably corrupt snapshot. Ignoring remainder of current snapshot file.");
                    break;
                }
                snapshots.Add(snapshot);
                memory.Position += snapshot.Length;
            }
            return snapshots;
        }

        private void TryClose()
        {
            if (fs != null)
            {
                fs.Close();
                if( debug) log.Debug("Closed " + storeName);
            }
        }

        public bool Recover()
        {
            lock( fileLocker)
            {
                TryClose();
                var files = FindSnapshotFiles();
                var loaded = false;
                foreach (var file in files)
                {
                    if (debug) log.Debug("Attempting recovery from snapshot file: " + file.Filename);
                    SnapshotReadAll(file.Filename);
                    var snapshots = SnapshotScan();
                    for (var i = snapshots.Count - 1; i >= 0; i--)
                    {
                        var snapshot = snapshots[i];
                        if (debug) log.Debug("Trying snapshot at offset: " + snapshot.Offset + ", length: " + snapshot.Length);
                        if (SnapshotLoadLast(snapshot))
                        {
                            if (debug) log.Debug("Snapshot successfully loaded.");
                            loaded = true;
                            break;
                        }
                    }
                    if (loaded)
                    {
                        break;
                    }
                }
                if (loaded)
                {
                    forceSnapShotRollover = true;
                }
                return loaded;
            }
        }

        private bool forceSnapShotRollover = false;

        private bool SnapshotLoadLast(Snapshot snapshot) {

            try
            {
                uniqueIds.Clear();
                replaceIds.Clear();
                originalIds.Clear();

                memory.Position = snapshot.Offset + sizeof(Int32); // Skip the snapshot length;

                remoteSequence = reader.ReadInt32();
                localSequence = reader.ReadInt32();
                lastSequenceReset = new TimeStamp(reader.ReadInt64());

                int orderCount = reader.ReadInt32();
                for (var i = 0; i < orderCount; i++)
                {

                    var id = reader.ReadInt32();
                    var action = (OrderAction) reader.ReadInt32();
                    var brokerOrder = reader.ReadInt64();
                    var logicalOrderId = reader.ReadInt32();
                    var logicalSerialNumber = reader.ReadInt64();
                    var orderState = (OrderState)reader.ReadInt32();
                    var price = reader.ReadDouble();
                    var flags = (OrderFlags) reader.ReadInt32();
                    var replaceId = reader.ReadInt32();
                    var originalId = reader.ReadInt32();
                    var side = (OrderSide)reader.ReadInt32();
                    var size = reader.ReadInt32();
                    var symbol = reader.ReadString();
                    var tag = reader.ReadString();
                    if (string.IsNullOrEmpty(tag)) tag = null;
                    var type = (OrderType)reader.ReadInt32();
                    var utcCreateTime= new TimeStamp(reader.ReadInt64());
                    var lastStateChange = new TimeStamp(reader.ReadInt64());
                    var sequence = reader.ReadInt32();
                    var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
                    var order = Factory.Utility.PhysicalOrder(action, orderState, symbolInfo, side, type, flags, price, size,
                                                              logicalOrderId, logicalSerialNumber, brokerOrder, tag, utcCreateTime);
                    order.ResetLastChange(lastStateChange);
                    order.Sequence = sequence;
                    uniqueIds.Add(id, order);
                    if (replaceId != 0)
                    {
                        replaceIds.Add(id, replaceId);
                    }
                    if( originalId != 0)
                    {
                        originalIds.Add(id, originalId);
                    }
                }

                foreach (var kvp in replaceIds)
                {
                    var orderId = kvp.Key;
                    var replaceId = kvp.Value;
                    uniqueIds[orderId].ReplacedBy = uniqueIds[replaceId];
                }

                foreach (var kvp in originalIds)
                {
                    var orderId = kvp.Key;
                    var originalId = kvp.Value;
                    uniqueIds[orderId].OriginalOrder = uniqueIds[originalId];
                }

                ordersByBrokerId.Clear();
                ordersBySequence.Clear();
                ordersBySerial.Clear();
                
                foreach (var kvp in uniqueIds)
                {
                    var order = kvp.Value;
                    ordersByBrokerId[order.BrokerOrder] = order;
                    ordersBySequence[order.Sequence] = order;
                    if( order.Action == OrderAction.Cancel && order.OriginalOrder == null)
                    {
                        throw new ApplicationException("Cancel order w/o any original order setting: " + order);
                    }
                    if (order.Action == OrderAction.Change && order.OriginalOrder == null)
                    {
                        throw new ApplicationException("Change order w/o any original order setting: " + order);
                    }
                }

                var bySerialCount = reader.ReadInt32();
                for (var i = 0; i < bySerialCount; i++)
                {
                    var logicalSerialNum = reader.ReadInt64();
                    var orderId = reader.ReadInt32();
                    var order = uniqueIds[orderId];
                    ordersBySerial[order.LogicalSerialNumber] = order;
                }

                using( positionsLocker.Using())
                {
                    var positionCount = reader.ReadInt32();
                    positions = new Dictionary<long, SymbolPosition>();
                    for (var i = 0L; i < positionCount; i++ )
                    {
                        var symbolString = reader.ReadString();
                        var symbol = Factory.Symbol.LookupSymbol(symbolString);
                        var position = reader.ReadInt64();
                        var symbolPosition = new SymbolPosition {Position = position};
                        positions.Add(symbol.BinaryIdentifier, symbolPosition);
                    }

                    strategyPositions = new Dictionary<int, StrategyPosition>();
                }
                return true;
            }
            catch( Exception ex)
            {
                log.Info("Loading snapshot at offset " + snapshot.Offset + " failed due to " + ex.Message + ". Rolling back to previous snapshot.", ex);
                return false;
            }
        }

        public void Clear()
        {
            if( debug) log.Debug("Clearing all orders.");
            ordersByBrokerId.Clear();
            ordersBySequence.Clear();
            ordersBySerial.Clear();
        }

        public void UpdateLocalSequence(int localSequence)
        {
            AssertAtomic();
            this.localSequence = localSequence;
        }

        public void UpdateRemoteSequence(int remoteSequence)
        {
            AssertAtomic();
            this.remoteSequence = remoteSequence;
        }

        public void SetSequences(int remoteSequence, int localSequence)
        {
            AssertAtomic();
            this.remoteSequence = remoteSequence;
            this.localSequence = localSequence;
        }

        public void ForceSnapshot()
        {
            if (writeFileResult != null && !writeFileResult.IsCompleted)
            {
                if (debug) log.Debug("ForceSnapshot() - snapshot in progress. Waiting before beginning another snapshot...");
                WaitForSnapshot();
            }
            if (anySnapShotWritten)
            {
                if (IsBusy)
                {
                    if (debug) log.Debug("ForceSnapshot() - snapshot already started. Waiting before beginning another snapshot...");
                    WaitForSnapshot();
                }

                if (debug) log.Debug("ForceSnapshot() - starting snapshot now...");
                RequestSnapshot();
                StartSnapShot();
                WaitForSnapshot();

                if (debug) log.Debug("ForceSnapshot() - snapshot complete.");
            }
        }

        private volatile bool isDisposed = false;
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (!isDisposed)
                {
                    isDisposed = true;
                    if( debug) log.Debug("Dispose()");
                    lock( snapshotLocker)
                    {
                        ForceSnapshot();
                        TryClose();
                    }
                }
            }
        }
    }
}