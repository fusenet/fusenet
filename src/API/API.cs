using System;
using System.IO;
using System.Net;
using System.Xml;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.Specialized;

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
using Fusenet.Utils;

namespace Fusenet.API
{
    public interface Api
    {
        int Count{ get; }
        bool Remove(int ID);
        List<int> Items { get; }
    }

    public class ServerList : Api
    {
        private Scheduler zServers;

        internal ServerList(Scheduler lServers)
        {
            zServers = lServers;
        }

        public string XML { get { lock (zServers) { return zServers.XML; } } }
        public int Count { get { lock (zServers) { return zServers.Count; } } }
        public bool Remove(int ID) { lock (zServers) { return zServers.Remove(ID); } }
        public List<int> Items { get { lock (zServers) { return zServers.ListID(); } } }
        //internal VirtualServer Get(int ID) { lock (zServers) { return zServers.Item(ID); } }

        public int Add(string Host, string Username = "", string Password = "", int Port = 119, int Connections = 1, bool SSL = false, ServerPriority Priority = ServerPriority.Default)
        {
            lock (zServers) 
            {
                VirtualServer vSched = zServers.Add(Host, Username, Password, Port, Connections, SSL, Priority);
                if (vSched == null) { return -1; }
                return vSched.ID;
            }
        }

    } // <fDOJvbbj9ws>

    public class SlotList : Api
    {
        private Scheduler zServers;

        internal SlotList(Scheduler lServers)
        {
            zServers = lServers;
        }

        public string XML { get { lock (zServers) { return zServers.Slots.XML; } } }
        public int Count { get { lock (zServers) { return zServers.Slots.Count; } } }
        public bool Remove(int ID) { lock (zServers) { return zServers.Slots.Remove(ID); } }
        public List<int> Items { get { lock (zServers) { return zServers.Slots.ListID(); } } }
        //internal VirtualSlot Get(int ID) { lock (zServers) { return zServers.Slots.Item(ID); } }

        //public string Add(string Name, Stream NZB)
        //{
        //    // Asynchronous, returns the Slot ID

        //    CancellationTokenSource vToken = null;

        //    if (NZB == null) { return ""; }
        //    if (Name == null) { return ""; }
        //    if (Name.Length == 0) { return ""; }

        //    //string SID = Module.RandomString(6);

        //    Action aStart = (Action)(() => ImportNZB(Name, SID, zServers.Slots, NZB, vToken));
        //    Task zAdd = new Task(aStart, vToken.Token);
        //    zAdd.Start();

        //    return SID;
        //}

        //public string Add(string Name, List<string> Commands)
        //{
        //    // Asynchronous, returns the Slot ID

        //    int cI = 1;

        //    if (Commands == null) { return ""; }
        //    if (Commands.Count == 0) { return ""; }

        //    NNTPInput nI = new NNTPInput(Name);
        //    List<NNTPInput> cList = new List<NNTPInput>();

        //    foreach (string sC in Commands)
        //    {
        //        if (sC == null) { continue; }
        //        if (sC.Length == 0) { continue; }

        //        NNTPSegment nS = new NNTPSegment();

        //        nS.Index = cI;
        //        nS.Command = sC;
        //        nI.Segments.Add(nS);
        //    }

        //    if (nI.Segments.Count == 0) { return ""; }

        //    cList.Add(nI);

        //    //string SID = Module.RandomString(6);
        //    CancellationTokenSource vToken = new CancellationTokenSource(); 

        //    Action aStart = (Action)(() => InternalAdd(nI.Name, SID, zServers.Slots, cList, vToken.Token));
        //    Task zAdd = new Task(aStart, vToken.Token);
        //    zAdd.Start();

        //    return SID;
        //}

        public string Send(string Newsgroup, List<string> Command, CancellationTokenSource vToken = null)
        {
            // Synchronous, returns the last commands response

            if (Newsgroup == null) { throw new Exception("No group"); }
            if (Command == null) { throw new Exception("No command"); }
            if (zServers.Count == 0) { throw new Exception("No server"); }

            List<string> tList = new List<string>();

            tList.Add("GROUP " + Newsgroup.ToLower());
            tList.AddRange(Command);

            NNTPSegment nS = new NNTPSegment();
            NNTPInput nI = new NNTPInput("");
            List<NNTPInput> cList = new List<NNTPInput>();

            nS.Index = 1;
            nS.Commands = tList;

            nI.Segments.Add(nS);
            cList.Add(nI);

            ManualResetEventSlim wHandle;
            wHandle = new ManualResetEventSlim(false);

            if (vToken == null) { vToken = new CancellationTokenSource(); }

            VirtualSlot vSlot = InternalAdd(nI.Name, zServers.Slots, cList, vToken.Token, wHandle);

            if (vSlot == null) { throw new Exception("No slot"); }
            if (vToken.Token.IsCancellationRequested) { throw new Exception("Cancelled"); }

            List<WaitHandle> wList = new List<WaitHandle>();

            wList.Add(vToken.Token.WaitHandle);

            if (vSlot.WaitHandle == null) { throw new Exception("No waithandle"); }
            
            wList.Add(vSlot.WaitHandle.WaitHandle);

            vSlot.Status = SlotStatus.Downloading;
            Notify();

            WaitHandle wRet = Common.WaitList(wList);

            if ((wRet == null) || (wRet.Handle == vToken.Token.WaitHandle.Handle))
            {
                throw new Exception("Cancelled");
            }

            if (vToken.Token.IsCancellationRequested) { throw new Exception("Cancelled"); }

            string zOut = null;

            if (vSlot.Status != SlotStatus.Completed)
            {
                if (vSlot.Status == SlotStatus.Failed)
                {
                    zOut = vSlot.StatusLine;
                }
                else
                {
                    zOut = Common.TranslateStatus((int)vSlot.Status);
                }

                int vId = vSlot.ID;
                vSlot = null;

                Remove(vId);

                if ((zOut == null) || (zOut.Length == 0))
                {
                    zOut = "Unknown";
                }

                throw new Exception(zOut);
            }
            else
            {
                foreach (VirtualFile vFile in vSlot.List())
                {
                    if (vFile.Output == null) { continue; }
                    if (vFile.Output.Data == null) { continue; }
                    if (vFile.Output.Data.Length == 0) { continue; }

                    vFile.Output.Data.Position = 0;
                    zOut = Common.GetString(vFile.Output.Data);
                }

                int vId = vSlot.ID;
                vSlot = null;

                Remove(vId);
                return zOut;
            }
       }

        //private void ImportNZB(string Name, string SID, Slots zSlots, Stream xXML, CancellationTokenSource vToken = null)
        //{
        //    if (vToken == null) { vToken = new CancellationTokenSource(); }

        //    InternalAdd(Name, SID, zSlots, NZB.Parse(xXML), vToken.Token);
        //    xXML.Close();
        //}

        private VirtualSlot InternalAdd(string Name, Slots zSlots, List<NNTPInput> cList, CancellationToken vToken, ManualResetEventSlim wHandle = null)
        {
            try
            {
                lock (zServers)
                {
                    return zSlots.Add(Name, cList, vToken, wHandle);
                }
            }
            catch { }

            return null;
        }

        private void Notify()
        {
            try
            {
                lock (zServers)
                {
                    foreach (VirtualConnection vCon in zServers.Connections.List(-1))
                    {
                        vCon.Idle.Set(); // Wakey wakey
                    }
                }
            }
            catch { }

            return;
        }

    } // <nAuIfDIHWIs>

    //internal class WebHandler : Phocus.Webhandler
    //{
    //    private Engine Engine;

    //    internal WebHandler(Engine zEngine)
    //    {
    //        Engine = zEngine;
    //    }

    //    public string VirtualDirectory
    //    {
    //        get { return "api"; }
    //    }

    //    private string Execute(HTTP.Net.HttpResponse Response, string sMode, NameValueCollection qString)
    //    {
    //        string sOut = "";

    //        if (sMode != null)
    //        {
    //            switch (sMode)
    //            {
    //                case "queue":
    //                case "qstatus":
    //                    Response.ContentType = "text/xml";
    //                    sOut = Engine.XML;
    //                    break;

    //                case "addurl":

    //                    //addurl&name=http://www.example.com/example.nzb&nzbname=NiceName

    //                    string nzbname = qString.Get("name");
    //                    string nicename = qString.Get("nzbname");

    //                    if ((nzbname == null) || (nzbname.Length == 0))
    //                    {
    //                        sOut = "nok" + Environment.NewLine;
    //                        break;
    //                    }

    //                    if ((nicename == null) || (nicename.Length == 0))
    //                    {
    //                        nicename = "Unknown";
    //                    }

    //                    string xml = Module.DownloadString(nzbname);

    //                    if ((xml == null) || (xml.Length == 0))
    //                    {
    //                        sOut = "nok" + Environment.NewLine;
    //                        break;
    //                    }


    //                    break;

    //                case "file":

    //                    string sname = qString.Get("name");
    //                    string segxml = qString.Get("xml");

    //                    if ((sname == null) || (sname.Length == 0))
    //                    {
    //                        sOut = "nok" + Environment.NewLine;
    //                        break;
    //                    }

    //                    if ((segxml == null) || (segxml.Length == 0))
    //                    {
    //                        sOut = "nok" + Environment.NewLine;
    //                        break;
    //                    }

    //                    // TODO
    //                    sOut = "ok" + Environment.NewLine;
    //                    break;

    //                case "resume":
    //                    // TODO
    //                    sOut = "ok" + Environment.NewLine;
    //                    break;

    //                case "pause":
    //                    // TODO
    //                    sOut = "ok" + Environment.NewLine;
    //                    break;

    //                case "restart":
    //                    sOut = "ok" + Environment.NewLine;
    //                    break;

    //                case "shutdown":
    //                    Engine.Close();
    //                    sOut = "ok" + Environment.NewLine;
    //                    break;

    //                case "version":

    //                    StringBuilder sB = new StringBuilder();
    //                    XmlWriter xR = XmlWriter.Create(sB, Module.WriterSettings);
    //                    Version vrs = Assembly.GetExecutingAssembly().GetName().Version;

    //                    xR.WriteStartElement("versions");
    //                    xR.WriteElementString("version", vrs.Major + "." + vrs.Minor);
    //                    xR.WriteEndElement();
    //                    xR.Flush();

    //                    sOut = sB.ToString();
    //                    break;

    //                case "warnings":
    //                    break;

    //                case "":
    //                    sOut = Module.FormatMsg("No mode specified");
    //                    break;

    //                default:
    //                    sOut = Module.FormatMsg("Unknown mode: " + sMode);
    //                    break;
    //            }
    //        }

    //        return sOut;
    //    }

    //    public bool Process(HTTP.Net.HttpContext context)
    //    {
    //        byte[] b = null;
    //        string sOut = "";
    //        bool bVal = true;

    //        try
    //        {
    //            if (context.Request.QueryString.HasKeys())
    //            {
    //                string sMode = context.Request.QueryString.Get("mode");

    //                if (sMode != null)
    //                {
    //                    sOut = Execute(context.Response, sMode, context.Request.QueryString);

    //                }
    //            }

    //            if (sOut.Length == 0)
    //            {
    //                context.Response.ContentType = "text/xml";
    //                sOut = Engine.XML;
    //            }

    //        }
    //        catch (Exception ex)
    //        {
    //            sOut = Module.FormatMsg("API Error: " + ex.Message);
    //        }

    //        b = Encoding.UTF8.GetBytes(sOut);
    //        context.Response.ContentLength64 = b.Length;

    //        context.Response.OutputStream.Write(b, 0, b.Length);
    //        context.Response.OutputStream.Close();

    //        return bVal;
    //    }
    //} // <N99W8jjbnHw>

    internal static class ApiXML
    {
        internal static bool Slot(XmlWriter xR, VirtualSlot vSlot)
        {
            xR.WriteStartElement("slot");
            xR.WriteElementString("nzo_id", Convert.ToString(vSlot.ID));
            xR.WriteElementString("name", Common.CleanString(vSlot.Name));
            xR.WriteElementString("filename", Common.CleanString(vSlot.Name));
            xR.WriteElementString("status", Common.TranslateStatus((int)vSlot.Status));

            if (vSlot.Status == SlotStatus.Failed)
            {
                xR.WriteElementString("fail_message", Common.CleanString(vSlot.StatusLine));
            }

            if (!(vSlot.History))
            {
                NNTPInfo vInfo = vSlot.Info;
                int lSeconds = vInfo.SecondsLeft(vSlot.SpeedAverage, vSlot.TotalTime);

                string sLeft = "00:00:00";
                string ETA = Common.FormatDate(DateTime.UtcNow);

                if (lSeconds > 0)
                {
                    sLeft = Common.FormatElapsed(new TimeSpan(0, 0, lSeconds));
                    ETA = Common.FormatDate(DateTime.UtcNow.AddSeconds(lSeconds));
                }

                xR.WriteElementString("index", Convert.ToString(vSlot.Index));
                xR.WriteElementString("percentage", Convert.ToString(Math.Round(vInfo.Percentage, 0)));
                xR.WriteElementString("bytes", Convert.ToString(vInfo.Expected));
                xR.WriteElementString("kbpersec", String.Format(CultureInfo.InvariantCulture, "{0:0.00}", vSlot.Speed / (decimal)1000));
                xR.WriteElementString("mb", String.Format(CultureInfo.InvariantCulture, "{0:0.00}", Common.BytesToMegabytes(vInfo.Expected)));
                xR.WriteElementString("mbleft", String.Format(CultureInfo.InvariantCulture, "{0:0.00}", Common.BytesToMegabytes(vInfo.BytesLeft)));
                xR.WriteElementString("size", String.Format(CultureInfo.InvariantCulture, "{0:0.0}", Common.BytesToMegabytes(vInfo.Expected)) + " MB");
                xR.WriteElementString("eta", ETA);
                xR.WriteElementString("timeleft", sLeft);
                xR.WriteElementString("priority", "Normal");

            }
            else
            {
                if (vSlot.Status == SlotStatus.Failed)
                {

                }
                else
                {

                    //<slot>
                    //    <loaded>False</loaded>
                    //    <id>605</id>
                    //    <size>778.1 MB</size>
                    //    <pp>D</pp>
                    //    <completeness>0</completeness>
                    //    <nzb_name>Ubuntu.nzb</nzb_name>
                    //    <storage>X:\Apps\Ubuntu</storage>
                    //    <completed>1236646078</completed>
                    //    <downloaded>815878352</downloaded>
                    //    <report>00000000</report>
                    //    <path>\Ubuntu</path>
                    //    <bytes>815878352</bytes>
                    //</slot>

                }
            }

            xR.WriteEndElement();
            xR.Flush();

            return true;
        }

        internal static bool Slots(XmlWriter xR, Slots vSlots)
        {
            xR.WriteStartElement("queue");

            NNTPInfo vInfo = vSlots.Info;

            string sLeft = "00:00:00";
            string ETA = Common.FormatDate(DateTime.UtcNow);
            int lSeconds = vInfo.SecondsLeft(vSlots.SpeedAverage, vSlots.TotalTime);

            if (lSeconds > 0)
            {
                sLeft = new TimeSpan(0, 0, 0, lSeconds, 0).ToString("c");
                ETA = Common.FormatDate(DateTime.UtcNow.AddSeconds(lSeconds));
            }

            xR.WriteElementString("status", vSlots.Status);
            xR.WriteElementString("paused", Common.BoolToString(vSlots.Paused));
            xR.WriteElementString("mb", String.Format(CultureInfo.InvariantCulture, "{0:0.00}", Common.BytesToMegabytes(Math.Abs(vInfo.Expected))));
            xR.WriteElementString("mbleft", String.Format(CultureInfo.InvariantCulture, "{0:0.00}", Common.BytesToMegabytes(Math.Abs(vInfo.BytesLeft))));
            xR.WriteElementString("kbpersec", String.Format(CultureInfo.InvariantCulture, "{0:0.00}", vSlots.Speed / (decimal)1000));

            xR.WriteElementString("eta", ETA);
            xR.WriteElementString("timeleft", sLeft);

            xR.WriteElementString("uptime", Common.FormatElapsed(vSlots.Uptime));
            xR.WriteElementString("start", "0");
            xR.WriteElementString("limit", "0");
            xR.WriteElementString("speedlimit", "0");
            xR.WriteElementString("noofslots", Convert.ToString(vSlots.Count));

            xR.WriteElementString("have_warnings", Convert.ToString(vSlots.Log.Count));

            if (vSlots.Log.Count > 0) 
            {
                string sWarning = Common.ReadLog(vSlots.Log, 1).Replace(Environment.NewLine, "");
                xR.WriteElementString("last_warning", Common.CleanString(sWarning)); 
            }
           
            // TODO Server.Log
            foreach (VirtualSlot vSlot in vSlots.List())
            {
                if (!(Slot(xR, vSlot))) { return false; }
            }

            xR.WriteEndElement();            
            xR.Flush();

            return true;
        }

        internal static bool Server(XmlWriter xR, VirtualServer vServer)
        {
            xR.WriteStartElement("server");
            xR.WriteElementString("nzo_id", Convert.ToString((vServer.ID)));
            xR.WriteElementString("host", vServer.Host);
            xR.WriteElementString("port", Convert.ToString(vServer.Port));
            xR.WriteElementString("ssl", Common.BoolToString(vServer.SSL));
            xR.WriteElementString("username", Common.CleanString(vServer.Username));
            xR.WriteElementString("password", Common.Repeat("*", vServer.Password.Length));
            xR.WriteElementString("priority", TranslatePriority(vServer.Priority));
            xR.WriteElementString("connections", Convert.ToString(vServer.Connections.Count(vServer.ID)));
            xR.WriteEndElement();
            xR.Flush();

            return true;
        }

        private static string TranslatePriority(ServerPriority sP)
        {
            if (sP == ServerPriority.High) { return "high"; }
            if (sP == ServerPriority.Low) { return "low"; }

            return "default";
        }
    }
} // <cnVJxILGQcQ>>
