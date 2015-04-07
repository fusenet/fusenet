using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Collections;
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

using Fusenet.Core;
using Fusenet.NNTP;

namespace Fusenet.Utils
{
    internal enum ArticleEncoding : int
	{
		yEnc,
		None,
		Base64,
		UuEncode,
		Quoted,
		gZip,
		AutoDetect
	}

    internal enum ArticleCompression : int
	{
		Zip,
		Rar,
		None
	}

    internal enum NNTPStatus : int
	{
		Connecting,
		Authenticating,
		Singleline,
		Multiline,
		Closed
	}

    internal enum WorkStatus : int
	{
        Queued,
		Downloading,
		Completed,
		Decoded,
		Decompressed,
		Missing,
		Failed
	}

    internal enum SchedulerMode : int
    {
        Sequential,
        Synchronous
    }

    internal enum SlotStatus
    {
        Queued,
        Downloading,
        Decoding,
        Extracting,
        Verifying,
        Repairing,
        Paused,
        Completed,
        Failed,
    }

    internal enum ConnectionStatus
    {
        Enabled,
        Disabled,
    }

    public enum ServerPriority
    {
        High,
        Default,
        Low,
    }

    public enum NNTPCodes
    {
        DateFollows = 111,
        PostingAllowed = 200,
        PostingDisallowed = 201,
        StreamingOK = 203,
        GoodBye = 205,
        GroupSelected = 211,
        GroupsFollow = 215,
        XINDEXFollows = 218,
        ArticleFollows = 220,
        HeadFollows = 221,
        BodyFollows = 222,
        StatFollows = 223,
        XOVERFollows = 224,
        NewNewsFollows = 230,
        Transferred = 235,
        PleaseSend = 238,
        TransferredOK = 239,
        PostedOK = 240,
        Authenticated1 = 250,
        Authenticated2 = 281,
        XGTITLEFollows = 282,
        XTHREADFollows = 288,
        SendArticle = 335,
        SendPost = 340,
        ContinueAuth = 350,
        MoreAuthentication = 381,
        TooManyConnections = 400,
        GroupNotFound = 411,
        NoGroupSelected = 412,
        NoArticleSelected = 420,
        NoNext = 421,
        NoPrevious = 422,
        NumberNotFound = 423,
        IDNotFound = 430,
        TryLater = 431,
        NotWanted = 435,
        TryAgain = 436,
        DoNotTryAgain = 437,
        AlreadyHave = 438,
        TransferFailed = 438,
        PostingNotAllowed = 440,
        PostingFailed = 441,
        AuthRequired = 450,
        AuthRejected = 452,
        TransferDenied = 480,
        AuthFailed1 = 481,
        AuthFailed2 = 482,
        BadCommand = 500,
        BadSyntax = 501,
        PermissionDenied = 502,
        FatalError = 503,
        SocketSuccess = 900,
        SocketUnknown = 901,
        SocketInterrupted = 902,
        SocketAccessDenied = 903,
        SocketFault = 904,
        SocketInvalidArgument = 905,
        SocketTooManyOpenSockets = 906,
        SocketWouldBlock = 907,
        SocketInProgress = 908,
        SocketAlreadyInProgress = 909,
        SocketNotSocket = 910,
        SocketDestinationAddressRequired = 911,
        SocketMessageSize = 912,
        SocketProtocolType = 913,
        SocketProtocolOption = 914,
        SocketProtocolNotSupported = 915,
        SocketSocketNotSupported = 916,
        SocketOperationNotSupported = 917,
        SocketProtocolFamilyNotSupported = 918,
        SocketAddressFamilyNotSupported = 919,
        SocketAddressAlreadyInUse = 920,
        SocketAddressNotAvailable = 921,
        SocketNetworkDown = 922,
        SocketNetworkUnreachable = 923,
        SocketNetworkReset = 924,
        SocketConnectionAborted = 925,
        SocketConnectionReset = 926,
        SocketNoBufferSpaceAvailable = 927,
        SocketIsConnected = 928,
        SocketNotConnected = 929,
        SocketShutdown = 930,
        SocketTimedOut = 931,
        SocketConnectionRefused = 932,
        SocketHostDown = 933,
        SocketHostUnreachable = 934,
        SocketProcessLimit = 935,
        SocketSystemNotReady = 936,
        SocketVersionNotSupported = 937,
        SocketNotInitialized = 938,
        SocketDisconnecting = 939,
        SocketTypeNotFound = 940,
        SocketHostNotFound = 941,
        SocketTryAgain = 942,
        SocketNoRecovery = 943,
        SocketNoData = 944,
        SocketIOPending = 945,
        SocketOperationAborted = 946,
    }

    internal interface VirtualItem
    {
        int Count { get; }
        NNTPInfo Info { get; }
        bool WriteXML(XmlWriter xR);
        List<VirtualItem> VirtualList { get; }
    }

    internal interface IndexedObject
    {
        int ID { get; set; }
        int Index { get; set; }

        int CompareTo(object obj);
        int CompareTo(IndexedObject obj);
    } 

    internal class NNTPInput
    {
        private string zName;
        public List<NNTPSegment> Segments = new List<NNTPSegment>();
        internal NNTPInput(string sName) { zName = sName; }
        public string Name { get { return zName; } }
    } 

    internal class NNTPSegment : IComparable<NNTPSegment>, IComparable
    {
        public int Index = 0;
        public int ExpectedSize = 0;
        
        public string Command = "";
        public List<string> Commands = null;

        internal NNTPSegment() { }

        internal NNTPSegment(int Number, int Bytes, string MessageID)
        {
            Index = Number;
            ExpectedSize = Bytes;
            this.Command = "BODY <" + MessageID + ">";
        }

        public int CompareTo(object obj) { return CompareTo(obj as NNTPSegment); }
        public int CompareTo(NNTPSegment obj) { return this.Index.CompareTo(obj.Index); }
    }

    internal class NNTPError
    {
        public int Code = 0;
        public int Tries = 0;
        public string Log = "";
        public string Message = "";
    }

    internal class WorkArgs : EventArgs
    {
        private int iCode;
        private Stream bData;
        private string sMessage;

        public WorkArgs(Stream bDat) { bData = bDat; }

        public WorkArgs(int Code, string sMsg) 
        {
            iCode = Code;
            sMessage = sMsg; 
        }

        public int Code { get { return iCode; } }
        public Stream Data { get { return bData; } }
        public string Message { get { return sMessage; } }

    } // <BZ5P64z4ag>

    internal class Stats
    {
        private long zLastTime = 0;
        private long zLastReset = 0;
        private long zTotalTime = 0;

        private long zLastBytes = 0;
        private long zFakeBytes = 0;
        private long zTotalBytes = 0;

        internal long TotalTime
        {
            set { Common.Safe32(ref zTotalTime, value); }
            get { return Interlocked.Read(ref zTotalTime); }
        }

        internal long TotalBytes
        {
            set { Common.Safe32(ref zTotalBytes, value); }
            get { return Interlocked.Read(ref zTotalBytes); }
        }

        internal long FakeBytes
        {
            set { Common.Safe32(ref zFakeBytes, value); }
            get { return Interlocked.Read(ref zFakeBytes); }
        }

        internal long LastTime
        {
            set { Common.Safe32(ref zLastTime, value); }
            get { return Interlocked.Read(ref zLastTime); }
        }

        private long LastReset
        {
            set { Common.Safe32(ref zLastReset, value); }
            get { return Interlocked.Read(ref zLastReset); }
        }

        internal long LastBytes
        {
            set { Common.Safe32(ref zLastBytes, value); }
            get { return Interlocked.Read(ref zLastBytes); }
        }

        internal void ValidateCache()
        {
            if (DateTime.UtcNow.Subtract(new DateTime(LastReset)).TotalSeconds >= 5)
            {
                LastTime = 0;
                LastBytes = 0;
                LastReset = DateTime.UtcNow.Ticks;
            }
        }

        internal void Progress(long AddedBytes)
        {
            Common.Add32(ref zFakeBytes, AddedBytes);
        }

        internal void Statistics(long AddedBytes, long AddedTime)
        {
            ValidateCache();

            Common.Add32(ref zLastTime, AddedTime);
            Common.Add32(ref zTotalTime, AddedTime);

            Common.Add32(ref zLastBytes, AddedBytes);
            Common.Add32(ref zTotalBytes, AddedBytes);
        }

    } // <weSceqYmadY>

    internal static class Common
	{
        private static Random cRandom = new Random();
        private static Encoding cEnc = Encoding.GetEncoding("iso-8859-1");       

        internal static void Safe32(ref long sLong, long lVal)
		{
            if (lVal == Interlocked.Read(ref sLong)) { return; }

            long SetValue = 0;
			do { SetValue = Interlocked.Read(ref sLong);
			} while (SetValue != Interlocked.CompareExchange(ref sLong, lVal, SetValue));
		}

        internal static void Add32(ref long sLong, long Incr)
		{
			long initialValue = 0;
			long computedValue = 0;

			do {
				initialValue = Interlocked.Read(ref sLong);
				computedValue = initialValue + Incr;
			} while (initialValue != Interlocked.CompareExchange(ref sLong, computedValue, initialValue));
		}

        internal static void UpdateValue(ConcurrentDictionary<int, int> cCol, int lIndex, int lValue)
		{
			cCol.AddOrUpdate(lIndex , lValue, (key, oldValue) => oldValue + lValue);
		}

        //internal static string FormatMsg(string s)
        //{
        //    return ("<html><body><h2>" + s + "</h2></body></html>");
        //}

        internal static int GetValue(ConcurrentDictionary<int, int> cCol, int lIndex)
		{
			int lValue = -1;

            if (!(cCol.ContainsKey(lIndex))) { return -1; }
            while (! (cCol.TryGetValue(lIndex, out lValue)))
            { if (!(cCol.ContainsKey(lIndex))) { return -1; } }

			return lValue;
		}

        internal static Stream GetStream(string sData)
        {
            byte[] bd = cEnc.GetBytes(sData);
            MemoryStream ms = new MemoryStream(bd, false);           
            return ms;
        }

        internal static byte[] GetBytes(Stream bData)
        {
            byte[] bD = new byte[bData.Length];
            
            bData.Position = 0;
            bData.Read(bD, 0, bD.Length);

            return bD;
        }

        internal static string GetString(Stream bData)
        {
            return cEnc.GetString(GetBytes(bData));
        }

        internal static StreamReader GetReader(Stream bData)
        {
            return new StreamReader(bData, cEnc, false);
        }

        internal static string GetBOF(Stream bData, int iLength)
        {

            byte[] bBOF = new byte[iLength];

            bData.Seek(0, SeekOrigin.Begin);
            bData.Read(bBOF, 0, bBOF.Length);

            return cEnc.GetString(bBOF);

        }

        internal static string GetEOF(Stream bData, int iLength)
        {
            byte[] bEOF = new byte[iLength];

            bData.Seek(-1 * iLength, SeekOrigin.End);
            bData.Read(bEOF, 0, bEOF.Length);

            return cEnc.GetString(bEOF);
        }

        internal static string VbLeft(string sText, int iLength)
        {
            if (sText == null) { return ""; }
            if (iLength <= 0 || sText.Length == 0) { return ""; }
            if (sText.Length <= iLength) { return sText; }

            return sText.Substring(0, iLength);       
        }

        internal static string VbRight(string sText, int iLength)
        {
            if (sText == null) { return ""; }
            if (iLength <= 0 || sText.Length == 0) { return ""; }
            if (sText.Length <= iLength) { return sText; }

            return sText.Substring(sText.Length - iLength);        
        }

        internal static bool IsNumeric(object expression)
        {
            double testDouble;

            if (expression == null) { return false; }
            if (double.TryParse(expression.ToString(), out testDouble)) { return true; }

            return false;
        }

        internal static WaitHandle WaitList(List<WaitHandle> wList, int TimeOut = -1)
        {
            WaitHandle[] wHandles = wList.ToArray();

            int cI = WaitHandle.WaitTimeout;

            if (TimeOut < 1)
            {
                cI = WaitHandle.WaitAny(wHandles, TimeOut);
                if (cI == WaitHandle.WaitTimeout) { return null; }
            }
            else
            {
                cI = WaitHandle.WaitAny(wHandles);
            }

            return wHandles[cI];    
        }

        internal static int GetCode(string sLine)
        {
            int ti;

            if (!(Int32.TryParse(VbLeft(sLine, 3), out ti)))
            {
                return 512;
            }

            return ti;
        }

        internal static Random Random
        {
            get { return cRandom; } 
        }

        internal static string ReadLog(ConcurrentQueue<string> zLog, int MaxLines = -1)
        {
            StringBuilder sB = new StringBuilder();

            if (zLog.IsEmpty) { return null; } 
            IEnumerator<string> iE = zLog.GetEnumerator();

            int sPos = zLog.Count - MaxLines;
            if ((sPos < MaxLines) || (MaxLines == -1)) { sPos = 0; }

            int cI = 0;

            while (iE.MoveNext())
            {
                cI += 1;
                if (cI >= sPos) 
                {
                    string sAdd = iE.Current;

                    if (sAdd.Length > 0)
                    {
                        sB.AppendLine(""); 
                        sB.Append("\t" + sAdd);
                    }
                }
            }

            return sB.ToString();
        }

        internal static void XMLToWriter(XmlWriter xw, string xml)
        {
            byte[] dat = cEnc.GetBytes(xml);
            MemoryStream m = new MemoryStream();
            m.Write(dat, 0, dat.Length);
            m.Seek(0, SeekOrigin.Begin);
            XmlReader r = XmlReader.Create(m);

            while (r.Read())
            {
                switch (r.NodeType)
                {
                    case XmlNodeType.Element:

                        xw.WriteStartElement(r.Name);

                        if (r.HasAttributes)
                        {
                            for (int i = 0; i < r.AttributeCount; i++)
                            {
                                r.MoveToAttribute(i);
                                xw.WriteAttributeString(r.Name, r.Value);
                            }
                        }

                        if (r.IsEmptyElement) { xw.WriteEndElement(); }
                        break;

                    case XmlNodeType.EndElement:
                        xw.WriteEndElement();
                        break;

                    case XmlNodeType.Text:
                        xw.WriteString(r.Value);
                        break;
                }
            }
        }

        internal static string MakeMsg(string sCode, string sMsg)
        {
            StringBuilder sB = new StringBuilder();
            XmlWriterSettings xS = new XmlWriterSettings();

            xS.OmitXmlDeclaration = true;
            xS.Indent = true;
            xS.IndentChars = "\t";
            xS.Encoding = cEnc;

            XmlWriter xR = XmlWriter.Create(sB, xS);

            xR.WriteStartElement("warning");
            xR.WriteElementString("code", CleanString(sCode));
            xR.WriteElementString("date", DateTime.UtcNow.ToString("dd-mm-yyyy hh:MM:ss"));
            xR.WriteElementString("message", CleanString(sMsg));
            xR.WriteEndElement();
            xR.Flush();

            return sB.ToString();
        }

        internal static XmlWriterSettings WriterSettings
        {
            get
            {
                XmlWriterSettings Settings = new XmlWriterSettings();
                Settings.Indent = true;
                Settings.IndentChars = "\t";
                Settings.Encoding = cEnc;
                return Settings;
            }
        }

        internal static XmlReaderSettings ReaderSettings
        {
            get
            {
                XmlReaderSettings Settings = new XmlReaderSettings();
                Settings.DtdProcessing = DtdProcessing.Ignore;
                Settings.XmlResolver = null;
                return Settings;
            }
        }

        internal static string SafeString(string sIn, bool bSanitize = true)
        {
            string sStrip = sIn;

            if (((sStrip.Contains("<")) || (sStrip.Contains(">")) || (sStrip.Contains("&"))) && (bSanitize))
            {
                sStrip = sStrip.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            }

            return sStrip;
        }

        internal static List<int> EnumInt(IEnumerator iE)
        {
            List<int> cList = new List<int>();
            while (iE.MoveNext()) { cList.Add((int)iE.Current); }
            return cList;
        }

        internal static List<IndexedObject> EnumObj(IEnumerator iE)
        {
            List<IndexedObject> cList = new List<IndexedObject>();
            while (iE.MoveNext()) { cList.Add((IndexedObject)iE.Current); }
            return cList;
        }

        internal static List<string> EnumStr(IEnumerator iE)
        {
            List<string> cList = new List<string>();
            while (iE.MoveNext()) { cList.Add((string)iE.Current); }
            return cList;
        }

        internal static double BytesToMegabytes(long bytes)
        {
            return (bytes / 1024f) / 1024f;
        }

        internal static double KilobytesToMegabytes(long kilobytes)
        {
            return kilobytes / 1024f;
        }

        internal static string MostFrequent(IEnumerator inp)
        { 
            Dictionary<string, int> cCount = new Dictionary<string, int>();

            foreach (string line in Common.EnumStr(inp))
            {
                if (cCount.ContainsKey(line))
                {
                    cCount[line]++;
                }
                else
                {
                    cCount.Add(line, 1);
                }
            }

            IEnumerable Enum = cCount.OrderBy(x => x.Value).Reverse();
                                    
            foreach (KeyValuePair<string, int> kv in Enum)
            { 
                return kv.Key;
            }

            return "";
        }

        internal static string RandomString(int lLength)
        {   
            StringBuilder sB = new StringBuilder();

            for (int i = 0; i < lLength; i++) 
            {
                if (Common.Random.Next(0, 2) == 1)
                {
                    sB.Append((char)(Common.Random.Next(65, 90)));
                }
                else
                {
                    sB.Append((char)(Common.Random.Next(97, 122)));
                }
            }

            return sB.ToString();
        }

        internal static bool StringToBool(string sIn)
        {
            return (sIn.ToLower().Trim() == "true");
        }

        internal static string BoolToString(bool sIn)
        {
            if (sIn) { return "True"; }
            return "False";
        }

        //internal static string DownloadString(string URL)
        //{
        //    string sout = null;

        //    WebClient wb = new WebClient();

        //    try
        //    {
        //        sout = wb.DownloadString(URL);
        //    }
        //    catch
        //    {
        //        return null;
        //    }

        //    if (sout.Length > 0) { return sout; }

        //    return null;
        //}

        internal static string Repeat(string Input, int Count)
        {
            StringBuilder builder = new StringBuilder(
                (Input == null ? 0 : Input.Length) * Count);
            for (int i = 0; i < Count; i++) builder.Append(Input);
            return builder.ToString();
        }

        internal static string MakeErr(NNTPError zErr)
        {
            return (Convert.ToString(zErr.Code) + " " + zErr.Message);
        }

        internal static string FormatElapsed(TimeSpan ts)
        {
            string sFormat = string.Format("{0:00}:{1:00}:{2:00}:{3:00}", ts.Days, ts.Hours, ts.Minutes, ts.Seconds);
            if (sFormat.StartsWith("00:")) { sFormat = sFormat.Substring(3); }
            return sFormat;
        }

        internal static string FormatDate(DateTime dt)
        {
            return dt.ToString("HH:mm:ss ddd dd MMM", CultureInfo.CreateSpecificCulture("en-US"));
        }

        internal static string XmlToString(VirtualItem vi)
        {
            StringBuilder sX = new StringBuilder();
            XmlWriter xR = CreateWriter(sX);
            if (!(vi.WriteXML(xR))) { return ""; }
            return sX.ToString();
        }

        internal static XmlWriter CreateWriter(StringBuilder sX)
        {
            XmlWriter xR = XmlWriter.Create(sX, Common.WriterSettings);
            xR.WriteProcessingInstruction("xml", "version='1.0' encoding='ISO-8859-1'");
            return xR;
        }

        internal static NNTPInfo CountInfo(List<VirtualItem> cList)
        {
            long cA = 0, cT = 0, cE = 0,  cB = 0;

            foreach (VirtualItem zItem in cList)
            {
                if (zItem != null)
                {
                    NNTPInfo zInfo = zItem.Info;

                    cT += zInfo.Total;
                    cE += zInfo.Expected;
                    cA += zInfo.Available;
                    cB += zInfo.BytesDone;
                }
            }

            return new NNTPInfo(cA, cE, cT, cB);
        }

        internal static string TranslateStatus(int lStatus)
        {
            switch ((SlotStatus)lStatus)
            {
                case SlotStatus.Downloading:
                    return "Downloading";
                case SlotStatus.Paused:
                    return "Paused";
                case SlotStatus.Queued:
                    return "Queued";
                case SlotStatus.Failed:
                    return "Failed";
                case SlotStatus.Completed:
                    return "Completed";
                case SlotStatus.Decoding:
                    return "Decoding";
                case SlotStatus.Verifying:
                    return "Verifying";
                case SlotStatus.Repairing:
                    return "Repairing";
                case SlotStatus.Extracting:
                    return "Extracting";
                default:
                    return "Error";
            }
        }

        internal static NNTPError TranslateError(SocketError SockErr)
        {
            NNTPError eOut = new NNTPError();

            switch (SockErr)
            {
                case SocketError.Success:

                    eOut.Code = (int)NNTPCodes.SocketSuccess;
                    eOut.Message = "Success.";
                    break;

                case SocketError.Interrupted:

                    eOut.Code = (int)NNTPCodes.SocketInterrupted;
                    eOut.Message = "A blocking Socket call was canceled.";
                    break;

                case SocketError.AccessDenied:

                    eOut.Code = (int)NNTPCodes.SocketAccessDenied;
                    eOut.Message = "An attempt was made to access a Socket in a way that is forbidden by its access permissions.";
                    break;

                case SocketError.Fault:

                    eOut.Code = (int)NNTPCodes.SocketFault;
                    eOut.Message = "An invalid pointer address was detected by the underlying socket provider.";
                    break;

                case SocketError.InvalidArgument:

                    eOut.Code = (int)NNTPCodes.SocketInvalidArgument;
                    eOut.Message = "An invalid argument was supplied to a Socket member.";
                    break;

                case SocketError.TooManyOpenSockets:

                    eOut.Code = (int)NNTPCodes.SocketTooManyOpenSockets;
                    eOut.Message = "There are too many open sockets in the underlying socket provider.";
                    break;

                case SocketError.WouldBlock:

                    eOut.Code = (int)NNTPCodes.SocketWouldBlock;
                    eOut.Message = "An operation on a nonblocking socket cannot be completed immediately.";
                    break;

                case SocketError.InProgress:

                    eOut.Code = (int)NNTPCodes.SocketInProgress;
                    eOut.Message = "A blocking operation is in progress.";
                    break;

                case SocketError.AlreadyInProgress:

                    eOut.Code = (int)NNTPCodes.SocketAlreadyInProgress;
                    eOut.Message = "The nonblocking Socket already has an operation in progress.";
                    break;

                case SocketError.NotSocket:

                    eOut.Code = (int)NNTPCodes.SocketNotSocket;
                    eOut.Message = "A Socket operation was attempted on a non-socket.";
                    break;

                case SocketError.DestinationAddressRequired:

                    eOut.Code = (int)NNTPCodes.SocketDestinationAddressRequired;
                    eOut.Message = "A required address was omitted from an operation on a Socket.";
                    break;

                case SocketError.MessageSize:

                    eOut.Code = (int)NNTPCodes.SocketMessageSize;
                    eOut.Message = "The datagram is too long.";
                    break;

                case SocketError.ProtocolType:

                    eOut.Code = (int)NNTPCodes.SocketProtocolType;
                    eOut.Message = "The protocol type is incorrect for this Socket.";
                    break;

                case SocketError.ProtocolOption:

                    eOut.Code = (int)NNTPCodes.SocketProtocolOption;
                    eOut.Message = "An unknown, invalid, or unsupported option or level was used with a Socket.";
                    break;

                case SocketError.ProtocolNotSupported:

                    eOut.Code = (int)NNTPCodes.SocketProtocolNotSupported;
                    eOut.Message = "The protocol is not implemented or has not been configured.";
                    break;

                case SocketError.SocketNotSupported:

                    eOut.Code = (int)NNTPCodes.SocketSocketNotSupported;
                    eOut.Message = "The support for the specified socket type does not exist in this address family.";
                    break;

                case SocketError.OperationNotSupported:

                    eOut.Code = (int)NNTPCodes.SocketOperationNotSupported;
                    eOut.Message = "The address family is not supported by the protocol family.";
                    break;

                case SocketError.ProtocolFamilyNotSupported:

                    eOut.Code = (int)NNTPCodes.SocketProtocolFamilyNotSupported;
                    eOut.Message = "The protocol family is not implemented or has not been configured.";
                    break;

                case SocketError.AddressFamilyNotSupported:

                    eOut.Code = (int)NNTPCodes.SocketAddressFamilyNotSupported;
                    eOut.Message = "The address family specified is not supported. This error is returned if the IPv6 address family was specified and the IPv6 stack is not installed on the local machine. This error is returned if the IPv4 address family was specified and the IPv4 stack is not installed on the local machine.";
                    break;

                case SocketError.AddressAlreadyInUse:

                    eOut.Code = (int)NNTPCodes.SocketAddressAlreadyInUse;
                    eOut.Message = "Only one use of an address is normally permitted.";
                    break;

                case SocketError.AddressNotAvailable:

                    eOut.Code = (int)NNTPCodes.SocketAddressNotAvailable;
                    eOut.Message = "The selected IP address is not valid in this context.";
                    break;

                case SocketError.NetworkDown:

                    eOut.Code = (int)NNTPCodes.SocketNetworkDown;
                    eOut.Message = "The network is not available.";
                    break;

                case SocketError.NetworkUnreachable:

                    eOut.Code = (int)NNTPCodes.SocketNetworkUnreachable;
                    eOut.Message = "No route to the remote host exists.";
                    break;

                case SocketError.NetworkReset:

                    eOut.Code = (int)NNTPCodes.SocketNetworkReset;
                    eOut.Message = "The application tried to set KeepAlive on a connection that has already timed out.";
                    break;

                case SocketError.ConnectionAborted:

                    eOut.Code = (int)NNTPCodes.SocketConnectionAborted;
                    eOut.Message = "The connection was aborted by the .NET Framework or the underlying socket provider.";
                    break;

                case SocketError.ConnectionReset:

                    eOut.Code = (int)NNTPCodes.SocketConnectionReset;
                    eOut.Message = "The connection was reset by the remote peer.";
                    break;

                case SocketError.NoBufferSpaceAvailable:

                    eOut.Code = (int)NNTPCodes.SocketNoBufferSpaceAvailable;
                    eOut.Message = "No free buffer space is available for a Socket operation.";
                    break;

                case SocketError.IsConnected:

                    eOut.Code = (int)NNTPCodes.SocketIsConnected;
                    eOut.Message = "The Socket is already connected.";
                    break;

                case SocketError.NotConnected:

                    eOut.Code = (int)NNTPCodes.SocketNotConnected;
                    eOut.Message = "The application tried to send or receive data, and the Socket is not connected.";
                    break;

                case SocketError.Shutdown:

                    eOut.Code = (int)NNTPCodes.SocketShutdown;
                    eOut.Message = "A request to send or receive data was disallowed because the Socket has already been closed.";
                    break;

                case SocketError.TimedOut:

                    eOut.Code = (int)NNTPCodes.SocketTimedOut;
                    eOut.Message = "The connection attempt timed out, or the connected host has failed to respond.";
                    break;

                case SocketError.ConnectionRefused:

                    eOut.Code = (int)NNTPCodes.SocketConnectionRefused;
                    eOut.Message = "The remote host is actively refusing a connection.";
                    break;

                case SocketError.HostDown:

                    eOut.Code = (int)NNTPCodes.SocketHostDown;
                    eOut.Message = "The operation failed because the remote host is down.";
                    break;

                case SocketError.HostUnreachable:

                    eOut.Code = (int)NNTPCodes.SocketHostUnreachable;
                    eOut.Message = "There is no network route to the specified host.";
                    break;

                case SocketError.ProcessLimit:

                    eOut.Code = (int)NNTPCodes.SocketProcessLimit;
                    eOut.Message = "Too many processes are using the underlying socket provider.";
                    break;

                case SocketError.SystemNotReady:

                    eOut.Code = (int)NNTPCodes.SocketSystemNotReady;
                    eOut.Message = "The network subsystem is unavailable.";
                    break;

                case SocketError.VersionNotSupported:

                    eOut.Code = (int)NNTPCodes.SocketVersionNotSupported;
                    eOut.Message = "The version of the underlying socket provider is out of range.";
                    break;

                case SocketError.NotInitialized:

                    eOut.Code = (int)NNTPCodes.SocketNotInitialized;
                    eOut.Message = "The underlying socket provider has not been initialized.";
                    break;

                case SocketError.Disconnecting:

                    eOut.Code = (int)NNTPCodes.SocketDisconnecting;
                    eOut.Message = "A graceful shutdown is in progress.";
                    break;

                case SocketError.TypeNotFound:

                    eOut.Code = (int)NNTPCodes.SocketTypeNotFound;
                    eOut.Message = "The specified class was not found.";
                    break;

                case SocketError.HostNotFound:

                    eOut.Code = (int)NNTPCodes.SocketHostNotFound;
                    eOut.Message = "No such host is known. The name is not an official host name or alias.";
                    break;

                case SocketError.TryAgain:

                    eOut.Code = (int)NNTPCodes.SocketTryAgain;
                    eOut.Message = "The name of the host could not be resolved. Try again later.";
                    break;

                case SocketError.NoRecovery:

                    eOut.Code = (int)NNTPCodes.SocketNoRecovery;
                    eOut.Message = "The error is unrecoverable or the requested database cannot be located.";
                    break;

                case SocketError.NoData:

                    eOut.Code = (int)NNTPCodes.SocketNoData;
                    eOut.Message = "The requested name or IP address was not found on the name server.";
                    break;

                case SocketError.IOPending:

                    eOut.Code = (int)NNTPCodes.SocketIOPending;
                    eOut.Message = "The application has initiated an overlapped operation that cannot be completed immediately.";
                    break;

                case SocketError.OperationAborted:

                    eOut.Code = (int)NNTPCodes.SocketOperationAborted;
                    eOut.Message = "The overlapped operation was aborted due to the closure of the Socket.";
                    break;

                default:

                    eOut.Code = (int)NNTPCodes.SocketUnknown;
                    eOut.Message = "An unspecified Socket error has occurred.";
                    break;
            }

            return eOut;
        }

        internal static string CleanString(string sIn)
        {
            if (sIn == null) return null;

            byte[] bOut = cEnc.GetBytes(sIn);
            StringBuilder sOut = new StringBuilder();

            for (int i = 0; i < bOut.Length; i++)
            {
                if ((bOut[i] >= 32) && (bOut[i] < 127))
                {
                    sOut.Append((char)bOut[i]);
                }
                else
                {
                    sOut.Append("?");
                }
            }
            return sOut.ToString();
        }

	}
} // <cfaDckt-PKg>