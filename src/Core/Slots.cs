using System;
using System.Xml;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;

//-------------------------------------------------------------
//
//    Fusenet - The Future of Usenet
//              http://github.com/fusenet
//
//    This library is free software; you can redistribute it
//    and modify it under the terms of the GNU General Public
//    License as published by the Free Software Foundation.
//
//-------------------------------------------------------------

using Fusenet.API;
using Fusenet.Core;
using Fusenet.NNTP;
using Fusenet.Utils;

namespace Fusenet.Core
{
    internal class Slots : VirtualItem 
    {
        private long zUptime;
        private IndexedCollection zCol;
        internal ConcurrentQueue<string> Log;

        internal Slots()
        {
            zUptime = DateTime.UtcNow.Ticks;
            zCol = new IndexedCollection();
            Log = new ConcurrentQueue<string>();
        }

        public int Count { get { return zCol.Count; } }
        internal string XML { get { return Common.XmlToString(this); } }
        public NNTPInfo Info { get { return Common.CountInfo(VirtualList); } }
        internal bool ContainsKey(int SlotID) { return zCol.ContainsKey(SlotID); }
        internal List<int> ListID(int SlotID = -1) { return zCol.KeyList(SlotID); }
        internal VirtualSlot Item(int SlotID) { return (VirtualSlot)zCol.Item(SlotID); }

        internal List<VirtualSlot> List(int SlotID = -1)
        {
            return zCol.ObjectList(SlotID).Cast<VirtualSlot>().ToList();
        }

        internal List<VirtualSlot> ListStatus(SlotStatus cStatus)
        {
            List<VirtualSlot> sList = new List<VirtualSlot>();

            foreach(VirtualSlot vSlot in List())
            {
                if (vSlot.Status != cStatus) { continue; }
                sList.Add(vSlot);
            }

            return sList;
        }

        public List<VirtualItem> VirtualList
        {
            get { return List().Cast<VirtualItem>().ToList(); }
        }

        internal bool Remove(int SlotID = -1)
        {
            List<VirtualSlot> sList = List(SlotID);
            if (sList.Count == 0) { return true; }

            foreach (VirtualSlot vS in sList)
            {
                vS.Remove();
            }

            if (SlotID == -1)
            {
                zCol.Clear();
                return true;
            }
            else
            {
                return (zCol.Remove(SlotID) != null);
            }
        }

        internal TimeSpan Uptime
        {
            get { return DateTime.UtcNow.Subtract(new DateTime(Interlocked.Read(ref zUptime))); }
        }

        internal VirtualSlot Add(string Name, List<NNTPInput> cList, CancellationToken vToken, ManualResetEventSlim vWait)
        {
            if (cList == null) { return null; }
            if (Name == null) { return null; }

            if (cList.Count == 0) { return null; }

            List<VirtualFile> vFiles = new List<VirtualFile>();

            foreach (NNTPInput cC in cList)
            {
                List<NNTPSegment> Segs = cC.Segments;

                if (Segs == null) { continue; }
                if (Segs.Count == 0) { continue; }

                vFiles.Add(new VirtualFile(cC.Name, Segs));
            }

            if (vFiles.Count == 0) { return null; }
            if (vToken.IsCancellationRequested) { return null; }

            VirtualSlot vSlot = new VirtualSlot(Name, vFiles, vWait);

            if (!zCol.Add(vSlot)) { return null; }

            return vSlot;
        }

        internal string Status
        {
            get 
            { 
                if (Paused) { return "Paused"; } 
                if (Active) { return "Active"; }

                return "Idle";
            }
        }

        internal bool Paused
        {
            get
            {
                int iPauzed = ListStatus(SlotStatus.Paused).Count;

                if (Count == 0) return false;
                if (iPauzed == 0) return false;

                foreach(VirtualSlot vSlot in List())
                {
                    if (vSlot.History) { continue; }
                    if (vSlot.Status != SlotStatus.Paused) { return false; }
                }

                return true;
            }
        }

        internal bool Active
        {
            get
            {
                if (Count == 0) return false;

                foreach (VirtualSlot vSlot in List())
                {
                    if (vSlot.History) { continue; }
                    if (vSlot.Status != SlotStatus.Paused) { return true; }                   
                }

                return false;
            }
        }

        public bool WriteXML(XmlWriter xR)
        {
            if (Count == 0) { return false; }
            return ApiXML.Slots(xR, this);
        }

        public long Speed
        {
            get { return iSpeed(false); }
        }

        public long SpeedAverage
        {
            get { return iSpeed(true); }
        }

        public long TotalTime
        {
            get {   long cTime = 0;
                    foreach (VirtualSlot vSlot in ListStatus(SlotStatus.Downloading)) { cTime += vSlot.TotalTime; }
                    return cTime; }
        }

        private long iSpeed(bool Average)
        {
            long cSpeed = 0;

            foreach (VirtualSlot vSlot in ListStatus(SlotStatus.Downloading))
            {
                if (!Average) { cSpeed += vSlot.Speed; } else { cSpeed += vSlot.SpeedAverage; }
            }

            return cSpeed;
        }

    } // <v0WlDJAyeCE>

    internal class VirtualSlot : VirtualItem, IndexedObject 
    {
        private int zID;
        private int zIndex;
        private string zName;

        private Stats zStats;
        private IndexedCollection zCol;
        private ManualResetEventSlim zWait;

        private string zStatusLine = "";
        private int zSlotStatus = (int)SlotStatus.Queued;

        internal VirtualSlot(string sName, List<VirtualFile> cList, ManualResetEventSlim WaitHandle = null)
        {
            zName = sName;
            zWait = WaitHandle;
            zStats = new Stats();

            if (zWait == null) { zWait = new ManualResetEventSlim(); }
            zCol = new IndexedCollection(cList.Cast<IndexedObject>().ToList());
        }

        public int ID
        {
            get { return zID; }
            set { zID = value; }
        }

        public int Index
        {
            get { return zIndex; }
            set { zIndex = value; }
        }

        public int CompareTo(object obj) { return CompareTo(obj as IndexedObject); }
        public int CompareTo(IndexedObject obj) { return this.Index.CompareTo(obj.Index); }

        public string Name { get { return zName; } }
        public int Count { get { return zCol.Count; } }
        public long TotalTime { get { return zStats.TotalTime; } }
        public ManualResetEventSlim WaitHandle { get { return zWait; } }
        public NNTPInfo Info { get { return Common.CountInfo(VirtualList); } }
        public bool WriteXML(XmlWriter xR) { return (ApiXML.Slot(xR, this)); }
        internal void Progress(long AddedBytes) { zStats.Progress(AddedBytes); }
        internal List<int> ListID(int FileID = -1) { return zCol.KeyList(FileID); }
        internal VirtualFile Item(int FileID) { return (VirtualFile)zCol.Item(FileID); }
        public string StatusLine { get { return zStatusLine; } set { zStatusLine = value; } }
        public List<VirtualItem> VirtualList { get { return List().Cast<VirtualItem>().ToList(); } }
        internal List<VirtualFile> List(int FileID = -1) { return zCol.ObjectList(FileID).Cast<VirtualFile>().ToList(); }
        internal void Statistics(long AddedBytes, long AddedTime) { zStats.Statistics(AddedBytes, AddedTime); }

        internal SlotStatus Status
        {
            get { return (SlotStatus)zSlotStatus; }

            set
            {
                if (History) { return; }

                zSlotStatus = (int)value;

                if (value == SlotStatus.Completed || value == SlotStatus.Failed || value == SlotStatus.Paused)
                {
                    if (zWait != null)  { zWait.Set(); }
                }
            }
        }

        internal bool History
        {
            get
            {
                if (Status == SlotStatus.Failed) { return true; }
                if (Status == SlotStatus.Completed) { return true; }
                return false;
            }
        }

        internal string Log(int FileID = -1)
        {
            StringBuilder sB = new StringBuilder();

            sB.AppendLine("<warnings>");

            bool DidSome = false;

            foreach (VirtualFile vFile in List(FileID))
            {
                string sLog = vFile.Log;
                
                if (sLog != null) 
                {
                    DidSome = true;
                    sB.AppendLine(sLog); 
                }
            }

            if (!DidSome) { return ""; }

            sB.AppendLine("</warnings>");

            return sB.ToString();
        }

        internal bool Remove(int FileID = -1)
        {
            List<VirtualFile> sList = List(FileID);
            if (sList.Count == 0) { return true; }

            foreach (VirtualFile vS in sList)
            {
                vS.Remove();
            }

            if (FileID == -1)
            {
                zCol.Clear();
                StatusLine = "Removed";
                Status = SlotStatus.Failed;
                return true;
            }

            return zCol.Remove(FileID);
        }

        internal NNTPCommands Take(VirtualConnection vConnection)
        {
            object zObj = null;

            if (Status != SlotStatus.Downloading) { return null; }

            List<VirtualFile> sList = List();
            if (sList.Count == 0) { return null; }

            foreach (VirtualFile vF in sList)
            {
                while ((Item(vF.ID) != null) && (!vF.IsEmpty))
                {
                    vF.SlotID = ID; // Should be done earlier
                    zObj = vF.Take();

                    if (zObj == null)
                    {
                        if (vConnection.Cancelled) { return null; }
                        continue;
                    }

                    return (NNTPCommands)zObj;
                }
            }

            return null;
        }

        internal bool IsCompleted
        {
            get {   bool AllCompleted = true;
                    foreach (VirtualFile vC in List()) { if (!vC.IsCompleted) { AllCompleted = false; break; } }
                    return AllCompleted; }
        }

        internal bool IsDecoded
        {
            get  {  bool AllDecoded = true;
                    foreach (VirtualFile vC in List()) { if (!vC.IsDecoded) { AllDecoded = false; break; } }
                    return AllDecoded; } 
        }

        public long Speed
        {
            get { return iSpeed(zStats.LastBytes, zStats.LastTime); } // Bytes per second
        }

        public long SpeedAverage
        {
            get { return iSpeed(zStats.TotalBytes, zStats.TotalTime); } // Bytes per second
        }

        private long iSpeed(long LastBytes, long LastTime)
        {
            if (Status != SlotStatus.Downloading) { return 0; }
            if ((LastBytes == 0) || (LastTime == 0)) { return 0; }

            decimal TicksPerByte = (LastTime / (decimal)LastBytes);
            decimal Spd = TimeSpan.TicksPerSecond / TicksPerByte;

            return Convert.ToInt64(Math.Round(Spd, 0)); // Bytes per second
        }

    } // <qsXX0fGwScM>

    internal class VirtualFile : VirtualItem, IndexedObject
    {
        private int zID;
        private int sID;
        private int zIndex;

        private string zName;
        private long zTotal = 0;
        private long zExpected = 0;
        private bool bDecoded = false;

        private Stats zStats;
        private NNTPOutput zOutput;
        private IndexedCollection zCol;
        private ConcurrentQueue<string> ErrorLog;
        internal ConcurrentQueue<string> Errors;

        internal VirtualFile(string sName, List<NNTPSegment> cList)
        {
            zName = sName;
            zStats = new Stats();
            Errors = new ConcurrentQueue<string>();
            ErrorLog = new ConcurrentQueue<string>();
            zOutput = new NNTPOutput(cList.Count, zName);

            if (!Add(cList)) { throw new Exception("Add failed"); }
        }

        public string Name { get { return zName; } }
        public bool WriteXML(XmlWriter xR) { return false; }
        internal int Available { get { return zCol.Count; } }
        internal bool IsEmpty { get { return zCol.IsEmpty; } }
        internal NNTPOutput Output { get { return zOutput; } } 
        internal bool IsCompleted { get { return zCol.IsCompleted; } }
        public List<VirtualItem> VirtualList { get { return null; } }
        internal string Log { get { return Common.ReadLog(ErrorLog, 50); } }
        public int Count { get { return (int)Interlocked.Read(ref zTotal); } }
        internal void Progress(long AddedBytes) { zStats.Progress(AddedBytes); }
        internal bool Remove(int CommandID = -1) { return zCol.Remove(CommandID); }
        internal bool IsDecoded { get { return bDecoded; } set { bDecoded = value; } }
        internal void Statistics(long AddedBytes, long AddedTime) { zStats.Statistics(AddedBytes, AddedTime); }

        public int ID
        {
            get { return zID; }
            set { zID = value; }
        }

        public int Index
        {
            get { return zIndex; }
            set { zIndex = value; }
        }

        internal int SlotID
        {
            get { return sID; }
            set { sID = value; }
        }

        public int CompareTo(object obj) { return CompareTo(obj as IndexedObject); }
        public int CompareTo(IndexedObject obj) { return this.Index.CompareTo(obj.Index); }

        internal NNTPCommands Take() 
        {
            return (NNTPCommands)zCol.Take();
        }

        private List<NNTPCommands> List(int CommandID = -1)
        {
            return zCol.ObjectList(CommandID).Cast<NNTPCommands>().ToList();
        }

        private bool Add(List<NNTPSegment> cList)
        {
            long cSize = 0;
            List<IndexedObject> cOut = new List<IndexedObject>();

            if (cList == null) { return false; }
            if (cList.Count == 0) { return false; }

            Common.Safe32(ref zTotal, cList.Count);

            cList.Sort();

            foreach (NNTPSegment nS in cList)
            {
                if (nS.Commands != null)
                {
                    cOut.Add(new NNTPCommands(nS.Commands, this));
                }
                else
                {
                    cSize += nS.ExpectedSize;
                    if (nS.Command.Length == 0) { return false; }
                    List<string> cCom = new List<string>();
                    cCom.Add(nS.Command);
                    cOut.Add(new NNTPCommands(cCom, this, nS.ExpectedSize));
                }

            }

            Common.Safe32(ref zExpected, cSize);
            zCol = new IndexedCollection(cOut);
            return true;
        }

        public NNTPInfo Info
        {
            get { return new NNTPInfo((long)Available, Interlocked.Read(ref zExpected), Interlocked.Read(ref zTotal), zStats.FakeBytes);  }
        }

        internal void LogError(int CommandID, NNTPError zErr)
        {
            //Debug.WriteLine(Module.MakeMsg(Convert.ToString(zErr.Code), "Command #" + Convert.ToString(CommandID) + " - Error " + Module.MakeErr(zErr)));

            Errors.Enqueue(zErr.Message.Replace(Environment.NewLine, ""));
            ErrorLog.Enqueue(Common.MakeMsg(Convert.ToString(zErr.Code), "Command #" + Convert.ToString(CommandID) + " - Error " + Common.MakeErr(zErr)));
        }
    }
} // <Y5v6dd7WHTQ>