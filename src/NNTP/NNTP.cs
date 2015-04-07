using System;
using System.IO;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;

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

using Fusenet.Core;
using Fusenet.Utils;

namespace Fusenet.NNTP
{
    internal class VirtualNNTP
    {
        private VirtualServer iServer;
        private VirtualSocket iSocket;
        private VirtualConnection iConnection;

        private int StatusCode = 0;
        private string StatusLine = "";

        public event ClosedEventHandler Closed;
        public event ConnectedEventHandler Connected;
        public event ReceivedEventHandler Received;
        public event FailedEventHandler Failed;
        public event ResponseEventHandler Response;

        internal NNTPStatus SocketStatus = NNTPStatus.Closed;

        public delegate void ClosedEventHandler(VirtualNNTP sender);
        public delegate void ReceivedEventHandler(Stream sData, VirtualNNTP sender);
        public delegate void ConnectedEventHandler(int iCode, string sLine, VirtualNNTP sender);
        public delegate void ResponseEventHandler(int iCode, string sLine, VirtualNNTP sender);
        public delegate void FailedEventHandler(int iCode, string sError, string sLog, VirtualNNTP sender);

        ~VirtualNNTP()
        {
            Disconnect(997, "Cancelled", true);
        }

        internal VirtualNNTP(VirtualServer SVR, VirtualConnection VC)
        {
            iServer = SVR;
            iConnection = VC;
        }

        private void iConnected(object sender, WorkArgs e)
        {
            Receive();
        }

        private void iDisconnected(object sender, WorkArgs e)
        {
            iSocket = null;
            SocketStatus = NNTPStatus.Closed;

            //Debug.WriteLine("iDisconnected: " + e.Message);

            try
            {
                if ((e != null) && (Failed != null))
                {
                    Failed(e.Code, e.Message, iServer.Log, this);
                }
            }
            catch { }

            if (Closed != null) { Closed(this); }
        } 

        private bool Receive()
        {
            if (iConnection.Cancelled) { return false; }

            try 
            { 
                iSocket.Receive();
                return true;
            }
            catch (Exception ex) 
            { 
                Disconnect(990, "Receive: " + ex.Message, false);
                return false;
            }
        }

        private void iReceived(object sender, WorkArgs e)
        {
            string sBOF = "";
            string sEOF = "";

            byte lBOFLength = 3;
            byte lEOFLength = 5;

            bool bFinished = false;

            try
            {
                if (SocketStatus != NNTPStatus.Multiline)
                {
                    if (e.Data.Length >= lBOFLength)
                    {
                        sBOF = Common.GetBOF(e.Data, lBOFLength);

                        StatusCode = Common.GetCode(sBOF);

                        switch (StatusCode)
                        {
                            case (int)NNTPCodes.GroupsFollow:
                            case (int)NNTPCodes.XINDEXFollows:
                            case (int)NNTPCodes.ArticleFollows:
                            case (int)NNTPCodes.HeadFollows:
                            case (int)NNTPCodes.BodyFollows:
                            case (int)NNTPCodes.StatFollows:
                            case (int)NNTPCodes.XOVERFollows: 
                            case (int)NNTPCodes.NewNewsFollows: 
                            case (int)NNTPCodes.XGTITLEFollows:
                            case (int)NNTPCodes.XTHREADFollows:

                                SocketStatus = NNTPStatus.Multiline;
                                break;

                        }
                    }
                }

                int lPos = Convert.ToInt32(e.Data.Length - lEOFLength);

                if (lPos >= 0)
                {
                    sEOF = Common.GetEOF(e.Data, lEOFLength);

                    switch (SocketStatus)
                    {
                        case NNTPStatus.Multiline:

                            bFinished = (sEOF == (Environment.NewLine + "." + Environment.NewLine));
                            break;

                        default:

                            bFinished = (Common.VbRight(sEOF, 2) == Environment.NewLine);
                            break;
                    }
                }

                if (!bFinished)
                {
                    Receive();
                    return;
                }

                e.Data.Seek(0, SeekOrigin.Begin);
                Process(StatusCode, e.Data);

            }
            catch (Exception ex)
            {
                Disconnect(992, "Received: " + ex.Message, false);
            }

        } 

        private void Process(int NNTPCode, Stream bData)
        {
            string sHeader = "Binary (" + bData.Length + " bytes)";

            if (SocketStatus != NNTPStatus.Multiline)
            {
                sHeader = Common.GetReader(bData).ReadLine();
            }

            //iServer.WriteDebug(Module.VbLeft((Convert.ToString(NNTPCode) + "000"), 3), sHeader);

            if ((NNTPCode == (int)NNTPCodes.GoodBye) || (iConnection.Cancelled) || (NNTPCode == 512))
            {
                if (NNTPCode <= 0) { NNTPCode = 991; }
                Disconnect(NNTPCode, sHeader, iConnection.Cancelled);
                return;
            }

            switch (SocketStatus)
            {
                case NNTPStatus.Connecting:

                    switch (NNTPCode)
                    {
                        case (int)NNTPCodes.PostingAllowed:
                        case (int)NNTPCodes.PostingDisallowed:

                            StatusLine = sHeader;
                            SocketStatus = NNTPStatus.Authenticating;
                            SendLines("MODE READER");
                            return;

                        default:

                            if (NNTPCode <= 0) { NNTPCode = 991; }
                            Disconnect(NNTPCode, sHeader, true);
                            break;
                    }

                    break;

                case NNTPStatus.Authenticating:

                    switch (NNTPCode)
                    {
                        case (int)NNTPCodes.Authenticated1:
                        case (int)NNTPCodes.Authenticated2:

                            Ready(NNTPCode, StatusLine);
                            break;

                        case (int)NNTPCodes.MoreAuthentication:

                            SendLines("AUTHINFO PASS " + iServer.Password);
                            break;

                        case (int)NNTPCodes.PostingAllowed:
                        case (int)NNTPCodes.PostingDisallowed:

                            if (iServer.Username.Trim().Length == 0)
                            {
                                Ready(NNTPCode, StatusLine);
                            }
                            else
                            {
                                SendLines("AUTHINFO USER " + iServer.Username);
                            }
                            break;

                        case (int)NNTPCodes.AuthRequired:
                        case (int)NNTPCodes.TransferDenied:

                            SendLines("AUTHINFO USER " + iServer.Username);
                            break;

                        case (int)NNTPCodes.AuthRejected:
                        case (int)NNTPCodes.AuthFailed1:
                        case (int)NNTPCodes.AuthFailed2:

                        default:

                            if (NNTPCode <= 0) { NNTPCode = 991; }
                            Disconnect(NNTPCode, sHeader, true);
                            break;
                    }

                    break;

                case NNTPStatus.Singleline:

                    if (Response != null) { Response(NNTPCode, sHeader, this); }
                    break;

                case NNTPStatus.Multiline:

                    if (Received != null) { Received(bData, this); }
                    break;

                default:
                    Disconnect(993, "Status", true);
                    break;
            }
        }

        private void Ready(int StatusCode, string StatusLine)
        {
            SocketStatus = NNTPStatus.Singleline;
            if (Connected != null) { Connected(StatusCode, StatusLine, this); }
        }

        internal bool SendLines(string sCommand, int ExpectedBytesReturned = 0, bool bReceive = true)
        {
            try
            {
                //int CrPos = sCommand.IndexOf(Environment.NewLine);
                //if (CrPos < 1) { CrPos = sCommand.Length; }

                //iServer.WriteStatus(sCommand.Substring(0, CrPos));
                //iServer.WriteDebug("000", sCommand.Substring(0, CrPos));

                if (!sCommand.EndsWith(Environment.NewLine))
                {
                    sCommand += Environment.NewLine; 
                }

                if (iSocket == null)
                {
                    return false;
                }

                if (!iSocket.Send(Common.GetStream(sCommand), ExpectedBytesReturned))
                {
                    return false;
                }

                if (!bReceive) 
                {
                    return true;
                }

                return Receive(); 

            }
            catch (Exception ex)
            {
                Disconnect(994, "SendLine: " + ex.Message, false); 
                return false;
            }
        }

        public bool IsConnected
        {
            get
            {
                if (iSocket == null) { return false; }
                return iSocket.IsConnected();
            }
        }

        public bool Connect()
        {
            try
            {
                if (iConnection.Cancelled) { return false; }

                if (SocketStatus == NNTPStatus.Multiline)
                { SocketStatus = NNTPStatus.Singleline; }

                if (SocketStatus == NNTPStatus.Singleline)
                {
                    Ready(0, "");
                    return true;
                }

                SocketStatus = NNTPStatus.Connecting;

                if (iSocket == null) 
                {
                    if (!iServer.SSL)
                    {
                        iSocket = new Utils.Socket();
                    }
                    else
                    {
                        iSocket = new Utils.SSLSocket();
                    }

                    iSocket.Received += new EventHandler<WorkArgs>(iReceived);
                    iSocket.Connected += new EventHandler<WorkArgs>(iConnected);
                    iSocket.Disconnected += new EventHandler<WorkArgs>(iDisconnected);
                }

                return iSocket.Connect(iServer);

            }
            catch (Exception ex)
            {
                Disconnect(996, ex.Message, false);
                return false;
            }
        }

        internal bool Disconnect(int iCode, string sError, bool bSendQuit)
        {
            //Debug.WriteLine("Disconnect: " + sError);

            if (iSocket != null)
            {
                if ((bSendQuit) && (iCode != 970))
                {
                    if (iSocket.IsConnected())
                    {
                        if (!SendLines("QUIT", 0, false))
                        {
                            return true;
                        }
                    }
                }
            }

            if (iSocket != null)
            {
                if (iSocket.IsConnected())
                {
                    iSocket.Close(iCode, sError);
                    return true;
                }
            }

            iDisconnected(null, new WorkArgs(iCode, sError));
            return true;
        }

    } // <iqEPGAoYweY>

    internal class NNTPOutput
    {
        private int zTotal = 0;
        private int zIndex = 1;
        private int zOffset = 0;

        private Stream zOut;
        private bool bFinished;
        private string Filename;

        private IndexedCollection zCol;

        internal NNTPOutput(int lTotal, string sFile)
        {
            zTotal = lTotal;
            Filename = sFile;
            bFinished = false;

            zOut = new MemoryStream();
            zCol = new IndexedCollection(zTotal);
        }

        internal int Total { get { return zTotal; } }
        internal Stream Data { get { return zOut; } }
        internal bool Finished { get { return bFinished; } }

        internal bool Store(int lIndex, NNTPCommands nCom)
        {           
            if (lIndex != zIndex) 
            {
                return Queue(lIndex, nCom);
            }

            bool ret = Write(nCom);
            Interlocked.Increment(ref zIndex);

            if (ret)
            {
                while (zCol.ContainsKey(zIndex))
                {
                    NNTPCommands gCom = (NNTPCommands)zCol.Take();

                    if (gCom == null) { break; }
                    if (gCom.ID != zIndex) { break; }
                    
                    if (!Write(gCom)) { break; }
                    Interlocked.Increment(ref zIndex);
                }
            }

            if (zTotal == (zIndex - 1))
            {
                bFinished = true;
            }

            return ret;
        }

        private bool Write(NNTPCommands nCom)
        {
            if (nCom == null) { return false; }          
            if (nCom.Data == null)  { return true; }
            if (nCom.Status != WorkStatus.Completed) { return true; }

            if (nCom.Part != null)
            {
                if (zOffset > (nCom.Part.Begin - 1)) { return false; }

                if (zOffset < (nCom.Part.Begin - 1))
                {
                    int zCorrect = ((nCom.Part.Begin - 1) - zOffset) + 1;
                    byte[] zFill = new byte[zCorrect - 1];
                    zOut.Write(zFill, 0, zFill.Length);
                    Interlocked.Add(ref zOffset, (int)zFill.Length);
                }

                if (zOffset != (nCom.Part.Begin - 1)) { return false; }   
            }

            nCom.Data.Position = 0;
            nCom.Data.CopyTo(zOut);

            Interlocked.Add(ref zOffset, (int)nCom.Data.Length);           
            return true;
        }

        private bool Queue(int lIndex, NNTPCommands nCom)
        {
            if (zCol.ContainsKey(lIndex)) { return false; }
            return zCol.Add(lIndex, nCom);
        }

    } // <FuEZniyOsTw>

    internal class NNTPInfo
    {
        long zTotal = 0, zExpected = 0, zAvailable = 0, zBytesDone = 0;

        internal NNTPInfo(long Avail, long Expec, long lTotal, long lBytesDone)
        {
            zTotal = lTotal;
            zExpected = Expec;
            zAvailable = Avail;
            zBytesDone = lBytesDone;
        }

        internal int Total { get { return (int)Interlocked.Read(ref zTotal); } }
        internal long BytesDone { get { return Interlocked.Read(ref zBytesDone); } }
        internal long Expected { get { return Interlocked.Read(ref zExpected); } }
        internal int Available { get { return (int)Interlocked.Read(ref zAvailable); } }

        internal decimal Percentage
        {
            get
            {
                decimal lPercentage = 0;

                if (Total > 0) { lPercentage = 100 - ((Available / (decimal)Total) * 100); }
                if (Expected > 0) { lPercentage = (BytesDone / (decimal)Expected) * 100; }

                if (lPercentage < 0) { lPercentage = 0; }
                if (lPercentage > 100) { lPercentage = 100; }

                return lPercentage;
            }
        }

        internal long BytesLeft
        {
            get
            {
                if (Expected < 1) { return 0; }
                if (BytesDone >= Expected) { return 1; }
                return Expected - BytesDone;
            }
        }

        internal int SecondsLeft(long lSpeed, long lTotalTime)
        {
            int iLeft = 0;
            decimal Perc = Percentage;

            if ((Expected > 1) && (lSpeed > 0))
            {
                iLeft = (int)(BytesLeft / (decimal)lSpeed);
            }

            if (iLeft < 1) 
            {

                if (Perc == 0) { Perc = -1; }
                if (lTotalTime == 0) { return -1; }

                iLeft = (int)((((100 / Perc) * (decimal)lTotalTime) - lTotalTime) / (decimal)TimeSpan.TicksPerSecond);
            }

            if (iLeft < 1) { return 1; } else { return iLeft; } 
        }

    } // <lcvVhLTkdnY>

    internal class cNNTP : VirtualNNTP
    {
        private string LastGroup;
        private NNTPCommands cCommand;
        private VirtualServer vServer;
        private ManualResetEventSlim VirtualEvent = new ManualResetEventSlim();

        internal cNNTP(VirtualServer SVR, VirtualConnection VC) : base(SVR, VC)
        {
            vServer = SVR;

            this.Closed += new VirtualNNTP.ClosedEventHandler(VirtualEvents_Closed);
            this.Failed += new VirtualNNTP.FailedEventHandler(VirtualEvents_Failed);
            this.Received += new VirtualNNTP.ReceivedEventHandler(VirtualEvents_Received);
            this.Response += new VirtualNNTP.ResponseEventHandler(VirtualEvents_Response);
            this.Connected += new VirtualNNTP.ConnectedEventHandler(VirtualEvents_Connected);
        }

        public NNTPCommands ExecuteCommand(NNTPCommands zCommand, CancellationToken cCancel)
        {
            cCommand = zCommand;

            cCommand.Reset();
            cCommand.Status = WorkStatus.Downloading;

            VirtualEvent.Reset();

            if (!base.Connect())
            {
                Fail(980, "Connect");
                return cCommand;
            }

            VirtualEvent.Wait(5000, cCancel);

            if (cCancel.IsCancellationRequested) { return cCommand; }
 
            if (!VirtualEvent.IsSet)
            {
                if ((!base.IsConnected) || (base.SocketStatus == NNTPStatus.Closed) || (base.SocketStatus == NNTPStatus.Connecting))
                {
                    base.Disconnect((int)NNTPCodes.SocketTimedOut, Common.TranslateError(SocketError.TimedOut).Message, false);
                }

                VirtualEvent.Wait(-1, cCancel);
            }

            return cCommand;
        }

        private void VirtualEvents_Connected(int iCode, string sLine, VirtualNNTP sender)
        {
            if (iCode != 0) { LastGroup = null; }

            if (!SendNext())
            {
                Fail(965, "SendNext");
            }
        }

        private bool SendNext()
        {
            string sCom = cCommand.Next;

            if (sCom.Length == 0)
            {
                return false;
            }

            if (sCom.ToLower() == LastGroup)
            {
                if (!cCommand.Finished)
                {
                    sCom = cCommand.Next;
                }
            }

            return base.SendLines(sCom, cCommand.Expected, true);
        }

        private void VirtualEvents_Received(System.IO.Stream sData, VirtualNNTP sender)
        {
            Done(sData);
        }

        private void Done(System.IO.Stream sData)
        {
            sData.Position = 0;
            cCommand.Data = sData;
            cCommand.Status = WorkStatus.Completed;

            VirtualEvent.Set();
        }

        private void VirtualEvents_Failed(int iCode, string sError, string sLog, VirtualNNTP sender)
        {
            Fail(iCode, sError, sLog);
        }

        private void Fail(int iCode, string sError, string sLog = "")
        {
            LastGroup = null;

            if (cCommand == null) return;
            if (cCommand.Status == WorkStatus.Failed) return;
            
            if (iCode <= 0) { iCode = 983; }
            if (sError == null) { sError = ""; }
            if (sError.Length == 0) { sError = "Unknown"; }

            cCommand.Data = null;
            cCommand.Error.Code = iCode;
            cCommand.Status = WorkStatus.Failed;
            cCommand.Error.Log = sLog + Environment.NewLine;
            cCommand.Error.Message = sError + " (" + Convert.ToString(cCommand.Error.Code) + ")" + Environment.NewLine;

            VirtualEvent.Set();

            //Debug.WriteLine("Fail: " + sError);
        }

        private void VirtualEvents_Closed(VirtualNNTP sender)
        {
            LastGroup = null;
        }

        private void VirtualEvents_Response(int NNTPCode, string sLine, VirtualNNTP sender)
        {
            if ((cCommand != null) && (CommandOK(NNTPCode)))
            {
                if (NNTPCode == (int)NNTPCodes.GroupSelected)
                {
                    LastGroup = cCommand.Current.ToLower();
                }

                if (cCommand.Finished)
                {
                    Done(Common.GetStream(sLine));
                    return;
                }
                else
                {
                    if (SendNext()) { return; }

                    Fail(966, "SendNext");
                    return;                
                }
 
            }

            switch (NNTPCode)
            {
                case (int)NNTPCodes.GroupNotFound:
                case (int)NNTPCodes.NoGroupSelected:
                {
                    LastGroup = null;
                    break;
                }
            }
                
            switch (NNTPCode)
            {
                case (int)NNTPCodes.NoNext:
                case (int)NNTPCodes.NoPrevious:
                case (int)NNTPCodes.IDNotFound:
                case (int)NNTPCodes.NumberNotFound:
                case (int)NNTPCodes.GroupNotFound:
                case (int)NNTPCodes.NoGroupSelected:
                case (int)NNTPCodes.NoArticleSelected:
                case (int)NNTPCodes.PostingFailed:
                case (int)NNTPCodes.PostingNotAllowed:
                {
                    LastGroup = null;
                    Fail(NNTPCode, sLine);
                    
                    return;
                }
            }

            LastGroup = null;

            if (NNTPCode <= 0) { NNTPCode = 991; }
            base.Disconnect(NNTPCode, sLine, true);
        }

        private bool CommandOK(int NNTPCode)
        {
            switch (NNTPCode)
            {
                case (int)NNTPCodes.PostedOK:
                case (int)NNTPCodes.GroupSelected: 
                case (int)NNTPCodes.DateFollows:
                case (int)NNTPCodes.GroupsFollow:
                case (int)NNTPCodes.XINDEXFollows:
                case (int)NNTPCodes.ArticleFollows:
                case (int)NNTPCodes.HeadFollows:
                case (int)NNTPCodes.BodyFollows:
                case (int)NNTPCodes.StatFollows:
                case (int)NNTPCodes.XOVERFollows:
                case (int)NNTPCodes.NewNewsFollows:
                case (int)NNTPCodes.XGTITLEFollows:
                case (int)NNTPCodes.XTHREADFollows: 
                case (int)NNTPCodes.SendArticle:
                case (int)NNTPCodes.SendPost:

                    return true;

                default:

                    return false;
            }
        }
    }

}  // <v0WlDJAyeCE>
