using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Linq;
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
using Fusenet.NNTP;
using Fusenet.Utils;
using Fusenet.Core;

internal class Scheduler
{
    internal Connections Connections;
    internal Slots Slots = new Slots();

    private IndexedCollection zStacks = new IndexedCollection();
    private IndexedCollection zServers = new IndexedCollection();

    internal Scheduler()
    {
        Connections = new Connections(this);
    }

    ~Scheduler()
    {
        Close();
    }

    public int Count { get { return zServers.Count; } }
    private IndexedCollection Servers { get { return zServers; } }
    //internal List<string> ListSID() { return zServers.SIDList(-1); }
    private bool SlotExist(int SlotID) { return (Slots.ContainsKey(SlotID)); }
    private bool StackExist(int ServerID) { return zStacks.ContainsKey(ServerID); }
    private bool ServerExist(int ServerID) { return (zServers.ContainsKey(ServerID)); }
    internal List<int> ListID(int ServerID = -1) { return zServers.KeyList(ServerID); }
    internal VirtualServer Item(int ServerID) { return (VirtualServer)zServers.Item(ServerID); }
    //internal VirtualServer Item(string ServerSID) { return (VirtualServer)zServers.Item(ServerSID); }
    private List<VirtualServer> List(int ServerID = -1) { return zServers.ObjectList(ServerID).Cast<VirtualServer>().ToList(); }

    internal VirtualServer Add(string Host, string Username = "", string Password = "", int Port = 119, int MaxConnections = 1, bool SSL = false, ServerPriority Priority = ServerPriority.Default)
    {
        VirtualServer SrvInfo = new VirtualServer(Connections, Host, Username, Password, Port, MaxConnections, SSL, Priority);

        if (!zServers.Add(SrvInfo)) { return null; }

        for (int iC = 0; iC < SrvInfo.Allowed; iC++)
        {
            Connections.Add(SrvInfo.ID);
        }

        return SrvInfo;
    }

    internal bool Remove(int ServerID = -1)
    {
        if (ServerID == -1)
        {
            Connections.Clear();
            zServers.Clear();
            zStacks.Clear();

            return true;
        }

        Connections.RemoveServer(ServerID);
        zServers.Remove(ServerID);
        zStacks.Remove(ServerID);

        return true;
    }

    //internal bool Remove(string ServerSID = "")
    //{
    //    if (ServerSID.Length == 0) { return Remove(-1); }
    //    return Remove(zServers.GetID(ServerSID));
    //}

    internal IndexedCollection Stack(int ServerID, int SlotID)
    {
        if (!SlotExist(SlotID)) { return null; }
        if (Servers.Item(ServerID) == null) { return null; }

        if (!zStacks.ContainsKey(ServerID))
        {
            zStacks.Add(ServerID, new VirtualStack());
        }

        VirtualStack zItem = (VirtualStack)zStacks.Item(ServerID);
        return zItem.Stack(SlotID);
    }

    internal bool Close()
    {
        Slots.Remove(-1);
        Remove(-1);
        return true;
    }

    internal IndexedCollection SwitchStack(int SlotID, VirtualConnection vConnection)
    {
        IndexedCollection iStack = null;
        List<int> AvailableStacks = null;

        while(true)
        {
            if (!SlotExist(SlotID)) { return null; }
            if (vConnection.Cancelled) { return null; }

            AvailableStacks = SmartStack(vConnection);
            if (AvailableStacks.Count == 0) { return null; }

            foreach (int ServerID in AvailableStacks)
            {
                iStack = Stack(ServerID, SlotID);
                if (iStack != null) { return iStack; }
            }
        }
    }

    private bool ServerActive(int ServerID)
    {
        if (!zServers.ContainsKey(ServerID)) { return false; }
        return (Connections.List(ServerID, ConnectionStatus.Enabled).Count > 0);
    }

    private List<int> SmartStack(VirtualConnection vConnection)
    {
        int SmallestStack = 0;           
        List<int> ActiveSlots = WaitingSlots;
        List<int> AvailableStacks = new List<int>();

        foreach(VirtualServer vServer in List())
        {
            if (!ServerActive(vServer.ID)) { continue; }
            int iLoad = WorkLoad(vServer.ID, ActiveSlots);

            if (AvailableStacks.Count == 0 || iLoad <= SmallestStack)
            {
                if (SmallestStack > 0) { AvailableStacks.Clear(); }
                AvailableStacks.Add(vServer.ID);
                SmallestStack = iLoad;
            }
        }

        if ((AvailableStacks.Count == 0) && (ServerActive(vConnection.Server.ID)))
        {
            AvailableStacks.Add(vConnection.Server.ID);
        }

        return AvailableStacks;
    }

    private int WorkLoad(int ServerID, List<int> vSlots)
    {
        int iLoad = 0;

        if (vSlots == null) { return iLoad; }
        if (vSlots.Count == 0) { return iLoad; }
        if (!StackExist(ServerID)) { return iLoad; }

        foreach (int cID in vSlots)
        {
            VirtualSlot vSlot = Slots.Item(cID);

            if ((vSlot == null) || (vSlot.Status != SlotStatus.Downloading))
            {
                continue;
            }

            IndexedCollection tStack = Stack(ServerID, vSlot.ID);
            if (tStack != null) { iLoad += tStack.Count; }
        }

        return iLoad;
    }

    internal NNTPCommands FindWork(VirtualConnection vConnection)
    {
        //List<int> vCons;

        if (vConnection == null) { return null; }
        if (vConnection.Server == null) { return null; }

        NNTPCommands vCom = SearchRandomStack(vConnection);

        if (vCom != null) { return vCom; }
        if (vConnection.Server.Priority == ServerPriority.Low) { return null; }

        return SearchRandomSlot(vConnection);
    }

    private List<int> WaitingSlots
    {
        get
        {
            List<VirtualSlot> cList = Slots.ListStatus(SlotStatus.Downloading);
            if ((cList == null) || (cList.Count == 0)) { return new List<int>(); }

            List<int> iList = new List<int>(cList.Count);
            foreach (VirtualSlot vSlot in cList) { iList.Add(vSlot.ID); }

            return iList;
        }
    }

    private List<IndexedCollection> WaitingStacks(int ServerID)
    {
        List<IndexedCollection> cList = new List<IndexedCollection>();

        if (!StackExist(ServerID)) { return cList; }
        if (!ServerActive(ServerID)) { return cList; }

        foreach (VirtualSlot vSlot in RandomSlots())
        {
            IndexedCollection tStack = Stack(ServerID, vSlot.ID);
            if ((tStack != null) && (!tStack.IsEmpty)) { cList.Add(tStack); }
        }

        return cList;
    }

    private List<VirtualSlot> RandomSlots()
    {
        List<int> iList = WaitingSlots;
        List<VirtualSlot> sList = new List<VirtualSlot>();

        if (iList == null) { return null; }

        while (iList.Count > 0) 
        {
            int RandomID = iList[Common.Random.Next(0, iList.Count - 1)];

            VirtualSlot vSlot = Slots.Item(RandomID);

            if (iList.Contains(RandomID)) { iList.Remove(RandomID); }
            if ((vSlot == null) || (vSlot.Status != SlotStatus.Downloading)) { continue; }

            sList.Add(vSlot);
        }

        return sList;
    }
 
    private NNTPCommands SearchRandomSlot(VirtualConnection vConnection)
    {
        foreach(VirtualSlot vSlot in RandomSlots()) // Global queue
        {
            NNTPCommands zCommand = (NNTPCommands)vSlot.Take(vConnection);
            if (zCommand != null) { return zCommand; }
        }

        return null;
    }

    private NNTPCommands SearchRandomStack(VirtualConnection vConnection)
    {
        foreach (IndexedCollection vStack in WaitingStacks(vConnection.Server.ID)) 
        {
            NNTPCommands zCommand = (NNTPCommands)vStack.Take();
            if (zCommand != null) { return zCommand; }
        }

        return null;
    }

    internal bool WriteXML(XmlWriter xR)
    {
        if (Count == 0) { return false; }

        xR.WriteStartElement("servers");

        foreach (VirtualServer vServer in List())
        {
            if (!(vServer.WriteXML(xR))) { return false; }
        }

        xR.WriteEndElement();
        xR.Flush();
        return true;
    }

    internal string XML
    {
        get 
        {
            StringBuilder sX = new StringBuilder();
            XmlWriter xR = Common.CreateWriter(sX);

            if (!(WriteXML(xR))) { return ""; }

            return sX.ToString();
        }
    }

} // <d87hYr0mrfY>

internal class VirtualServer : IndexedObject 
{
    private int zID;
    private int zIndex;

    private string zHost;

    private int zPort;
    private bool zSSL;

    private string zUsername;
    private string zPassword;

    private int zConnections;
    private Connections zCon;
    private ServerPriority zPriority;

    private ConcurrentQueue<string> DebugLog = new ConcurrentQueue<string>();
    private ConcurrentQueue<string> StatusLog = new ConcurrentQueue<string>();

    internal VirtualServer(Connections lConnections, string Host, string Username = "", string Password = "", int Port = 119, int Connections = 1, bool SSL = false, ServerPriority Priority = ServerPriority.Default)
    {
        zSSL = SSL;
        zHost = Host;
        zPort = Port;
        zCon = lConnections;
        zPriority = Priority;
        zConnections = Connections;

        if (Username != null) { zUsername = Username; } else { zUsername = ""; };
        if (Password != null) { zPassword = Password; } else { zPassword = ""; };
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

    public string Host { get { return zHost; } }
    internal bool SSL { get { return zSSL; } }
    internal int Port { get { return zPort; } }
    internal string Username { get { return zUsername; } }
    internal string Password { get { return zPassword; } }
    internal int Allowed { get { return zConnections; } }
    internal ServerPriority Priority { get { return zPriority; } }
    internal Connections Connections { get { return zCon; }  }

    public string Log
    {
        get { return lStatus + Environment.NewLine + lDebug; }
    }

    private string lDebug
    {
        get { return Common.ReadLog(DebugLog, 500); }
    }

    private string lStatus
    {
        get { return Common.ReadLog(StatusLog, 500); }
    }

    internal void WriteDebug(string sCode, string sMsg)
    {
        //Debug.WriteLine("Debug: " + sMsg);
        DebugLog.Enqueue(Common.MakeMsg(sCode, sMsg));
    }

    internal void WriteStatus(string sMsg)
    {
        //Debug.WriteLine("Status: " + sMsg);
        StatusLog.Enqueue(Common.MakeMsg("000", sMsg));
    }

    internal void LogError(int CommandID, NNTPError zErr)
    {
        WriteStatus("Command #" + Convert.ToString(CommandID) + " - Error " + Common.MakeErr(zErr));
    }

    internal bool WriteXML(XmlWriter xR)
    {
        return ApiXML.Server(xR, this);
    }

} // <SL85HcP3Hzs>

internal class FileInfo
{
    private int zSize = 0;
    private string zFile = "";

    internal FileInfo(string Filename, int Size)
    {
        zSize = Size;
        zFile = Filename;
    }

    internal int Filesize { get { return zSize; } }
    internal string Filename { get { return zFile; } }
}

internal class PartInfo
{
    private int zEnd = 0;
    private int zBegin = 0;       

    internal PartInfo(int Begin, int End)
    {
        zBegin = Begin;
        zEnd = End;
    }

    internal int Begin { get { return zBegin; } }
    internal int End { get { return zEnd; } }
}

internal class NNTPCommands : IndexedObject 
{
    private int zID;
    private int zIndex;

    private int zSize;
    private int zStatus;
    private Stream zData;
    private FileInfo zFile;
    private PartInfo zPart;
    private VirtualFile zSeg;

    private int CommandIndex = -1;
    private List<string> zCommands = null;
    private NNTPError zError = new NNTPError();

    internal NNTPCommands(List<string> Commands, VirtualFile Segment, int ExpectedSize = 0)
    {
        zSeg = Segment;
        zCommands = Commands;
        zSize = ExpectedSize;
        zStatus = (int)WorkStatus.Queued;
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

    internal bool Finished
    {
        get 
        {
            if (zCommands == null)  { return true; }
            if (zCommands.Count == 0) { return true; }
            if (CommandIndex >= (zCommands.Count - 1)) { return true; }

            return false;
        }
    }

    internal string Next
    { 
        get 
        {
            if (Finished) { return ""; }

            CommandIndex++;
            return zCommands[CommandIndex];
        } 
    }

    internal string Current
    {
        get
        {
            if (CommandIndex < 0) { return ""; }
            if (CommandIndex >= zCommands.Count) { return ""; }

            return zCommands[CommandIndex];
        }
    }

    internal void Reset() { CommandIndex = -1; }
    public int Expected { get { return zSize; } }
    internal NNTPError Error { get { return zError; } }
    internal VirtualFile Segment { get { return zSeg; } }
    internal Stream Data { get { return zData; } set { zData = value; } }
    internal FileInfo File { get { return zFile; } set { zFile = value; } }
    internal PartInfo Part { get { return zPart; } set { zPart = value; } }

    internal WorkStatus Status
    {
        get { return (WorkStatus)(zStatus); }
        set { zStatus = (int)value; }
    }

    internal void LogError(NNTPError zErr, VirtualConnection vConnection)
    {
        if (zSeg != null)
        {
            zSeg.LogError(ID, zErr);
        }

        if (vConnection != null)
        {
            vConnection.LogError(ID, zErr);
        }
    }

    internal void Statistics(long AddedBytes, long RealTime, VirtualConnection vConnection)
    {
        if (zSeg != null) 
        {
            long lTime = Interlocked.Read(ref RealTime);
            long rBytes = Interlocked.Read(ref AddedBytes);

            zSeg.Statistics(rBytes, lTime);

            VirtualSlot vSlot = vConnection.Scheduler.Slots.Item(zSeg.SlotID);

            if (vSlot != null)
            {
                vSlot.Statistics(rBytes, lTime);
            }
        }
    }

    internal void Progress(long AddedBytes, VirtualConnection vConnection)
    {
        if (zSeg != null)
        {
            long rBytes = Interlocked.Read(ref AddedBytes);

            zSeg.Progress(rBytes);

            VirtualSlot vSlot = vConnection.Scheduler.Slots.Item(zSeg.SlotID);

            if (vSlot != null)
            {
                vSlot.Progress(rBytes);
            }                       
        }
    }

} // <GHlKdSI3_Zw>

internal class VirtualStack : IndexedObject 
{
    private int zID;
    private int zIndex;

    private IndexedCollection zCol = new IndexedCollection();

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

    public IndexedCollection Stack(int SlotID)
    {
        if (!zCol.ContainsKey(SlotID))
        {
            zCol.Add(SlotID, new IndexedCollection());
        }

        return (IndexedCollection)zCol.Item(SlotID);
    }
} // <qzu_-OGWEKE>
