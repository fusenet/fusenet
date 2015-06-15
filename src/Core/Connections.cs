using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Fusenet;


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

namespace Fusenet
{
    internal class Connections
    {
        private Scheduler zServers;
        private IndexedCollection zCol;

        internal Connections(Scheduler lServers)
        {
            zServers = lServers;
            zCol = new IndexedCollection();
        }

        internal VirtualConnection Item(int ConnectionID)
        {
            return (VirtualConnection)zCol.Item(ConnectionID);
        }

        internal List<int> ListID() { return zCol.KeyList(); }

        internal int Count(int ServerID = -1)
        {
            if (ServerID == -1) { return zCol.Count; }
            return List(ServerID).Count;
        }

        internal void Clear()
        {
            CancelConnection();
            zCol.Clear();
        }

        internal List<VirtualConnection> List(int ServerID = -1)
        {
            if (ServerID == -1)
            {
                return zCol.ObjectList().Cast<VirtualConnection>().ToList();
            }
            
            VirtualConnection vCon = null;
            List<VirtualConnection> cList = new List<VirtualConnection>();

            foreach(int iC in ListID())
            {
                vCon = Item(iC);

                if (vCon == null) { continue; }
                if (!(vCon.Server.ID == ServerID)) { continue; }

                cList.Add(vCon);
            }

            return cList;
        }

        internal List<int> List(int ServerID = -1, ConnectionStatus cStatus = ConnectionStatus.Enabled)
        {
            List<VirtualConnection> zList = List(ServerID);
            List<int> cList = new List<int>();

            foreach (VirtualConnection vCon in zList)
            {
                if (vCon.Status == cStatus)
                {
                    cList.Add(vCon.ID);
                }
            }
            return cList;
        }

        internal bool CancelConnection(int ConnectionID = -1)
        {
            List<VirtualConnection> zList = zCol.ObjectList(ConnectionID).Cast<VirtualConnection>().ToList();

            foreach (VirtualConnection vCon in zList)
            {
                vCon.Cancel();
            }

            return true;
        }

        internal bool RemoveServer(int ServerID = -1)
        {
            List<VirtualConnection> zList = List(ServerID);

            foreach (VirtualConnection vCon in zList)
            {
                RemoveConnection(vCon.ID);
            }

            return true;
        }

        internal bool RemoveConnection(int ConnectionID = -1)
        {
            bool bVal = false; 
            List<VirtualConnection> zList = zCol.ObjectList(ConnectionID).Cast<VirtualConnection>().ToList();

            foreach (VirtualConnection vCon in zList)
            {
                CancelConnection(vCon.ID);
                bVal = zCol.Remove(vCon.ID);

                if (ConnectionID != -1) { return bVal; }
            }

            return true;
        }

        internal VirtualConnection Add(int ServerID)
        {
            VirtualServer zServer = zServers.Item(ServerID);
            if (zServer == null) { return null; }

            VirtualConnection vCon = new VirtualConnection(zServers, zServer);
            if (!zCol.Add(vCon)) { return null; }

            vCon.Start();

            return vCon;
        }

    } // <7P-BHcV0_SQ>

    internal class VirtualConnection : Fusenet.IndexedObject 
    {
        private int zID;
        private int zIndex;

        private cNNTP zNNTP;
        private Scheduler zSched;
        private VirtualServer Srv;
        private ConnectionTask zConnection;
        private ManualResetEventSlim vIdle;
        private CancellationTokenSource vCancel;
        private int zStatus = (int)ConnectionStatus.Disabled;

        internal VirtualConnection(Scheduler Scheduler, VirtualServer cServer)
        {
            Srv = cServer;
            zSched = Scheduler;
            zNNTP = new cNNTP(cServer, this);

            zConnection = new ConnectionTask();
            vIdle = new ManualResetEventSlim();
            vCancel = new CancellationTokenSource();
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

        internal VirtualServer Server { get { return Srv; } }
        internal Scheduler Scheduler { get { return zSched; } }
        internal void Start() { zConnection.Task(this).Start(); ; }
        internal void Remove() { zSched.Connections.RemoveConnection(ID); }
        internal CancellationTokenSource Token { get { return vCancel; } }
        internal ManualResetEventSlim Idle { get { return vIdle; } }
        internal bool Cancelled { get { return (vCancel.IsCancellationRequested); } }

        internal void Cancel()
        {
            Enabled = false;

            if (vCancel != null)
            {
                vCancel.Cancel(true);
            }

            if (zNNTP != null)
            {
                cNNTP KeepRef = zNNTP;
                zNNTP = null;
                KeepRef.Disconnect(998, "Cancelled", true);
                KeepRef = null;
            }

            zConnection = null;

            if (vIdle != null)
            {
                vIdle.Set();
            }
        }

        internal ConnectionStatus Status
        {
            get
            {
                if (Cancelled) { zStatus = (int)ConnectionStatus.Disabled; }
                return (ConnectionStatus)zStatus;
            }
            set
            {
                if ((value == ConnectionStatus.Enabled) && (Cancelled)) { return; }
                zStatus = (int)value;
            }
        }

        internal NNTPCommands ExecuteCommand(NNTPCommands zCommand)
        {
            return zNNTP.ExecuteCommand(zCommand, Token.Token);
        }

        public bool Enabled
        {
            get { return (Status == ConnectionStatus.Enabled); }
            set
            {
                if (value) { Status = ConnectionStatus.Enabled; }
                else { Status = ConnectionStatus.Disabled; }
            }
        }

        internal void LogError(int CommandID, NNTPError zErr)
        {
            Srv.WriteStatus("Command #" + Convert.ToString(CommandID) + " - Error " + Common.MakeErr(zErr));
        }

    }
} // <vq0Yra8WYLo>
