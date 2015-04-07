using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;

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
    internal class ConnectionTask
    {
        private void Download(VirtualConnection vCon)
        {
            long LastTicks;
            NNTPCommands pWork = null;
            IndexedCollection SwitchStack;

            while (vCon.Enabled)
            {
                vCon.Token.Token.ThrowIfCancellationRequested();

                pWork = vCon.Scheduler.FindWork(vCon);
                
                if (pWork == null) { Wait(vCon); continue; }

                vCon.Token.Token.ThrowIfCancellationRequested();

                LastTicks = DateTime.UtcNow.Ticks;
                pWork = vCon.ExecuteCommand(pWork);

                vCon.Token.Token.ThrowIfCancellationRequested();

                if (pWork.Status == WorkStatus.Completed)
                {
                    pWork.Statistics(pWork.Data.Length, DateTime.UtcNow.Subtract(new DateTime(LastTicks)).Ticks, vCon);                    
                }
                else
                {
                    pWork.Statistics(0, DateTime.UtcNow.Subtract(new DateTime(LastTicks)).Ticks, vCon);

                    string sMsg = null;
                    bool bRet = HandleError(pWork, vCon);

                    if (!bRet)
                    {
                        // Never auto-shutdown the last connection
                        if (vCon.Scheduler.Connections.Count() < 2) 
                        { 
                            bRet = true;
                            pWork.Error.Tries += 1;
                        } 
                    }

                    if (!bRet) 
                    { 
                        vCon.Enabled = false;
                        sMsg = pWork.Error.Message;
                    }

                    if ((pWork.Error.Tries < (vCon.Scheduler.Count + 1)) || (!bRet))
                    {
                        SwitchStack = vCon.Scheduler.SwitchStack(pWork.Segment.SlotID, vCon);

                        if (SwitchStack != null)
                        {
                            pWork.Status = WorkStatus.Queued;
                            if (SwitchStack.Add(pWork) != null) { pWork = null; }
                        }
                    }

                    if (!bRet) { throw new Exception(sMsg); }

                }

                vCon.Token.Token.ThrowIfCancellationRequested();

                if (pWork != null)
                {
                    pWork.Progress(pWork.Expected, vCon);
                    Process(pWork, vCon);
                    pWork = null;
                }
            }
        }

        internal WaitHandle Wait(VirtualConnection vConnection, int lMilliseconds = 100)
        {
            List<WaitHandle> wList = new List<WaitHandle>();

            wList.Add(vConnection.Idle.WaitHandle);
            wList.Add(vConnection.Token.Token.WaitHandle);

            WaitHandle wHandle = Common.WaitList(wList, lMilliseconds);

            if ((wHandle != null) && (wHandle.Handle == vConnection.Idle.WaitHandle.Handle))
            { 
                vConnection.Idle.Reset(); 
            }

            return wHandle;
        }

        private void Main(VirtualConnection vCon)
        {
            vCon.Enabled = true;

            try
            {
                Download(vCon);
            }
            catch (Exception ex)
            {
                if (!(vCon.Cancelled)) { vCon.Server.WriteStatus("Error: " + ex.Message); }
            }

            if (vCon.Cancelled) { vCon.Server.WriteStatus("Cancelled"); }

            vCon.Enabled = false;
            vCon.Remove();

            if (vCon.Scheduler.Connections.Count() == 0)
            {
                foreach (VirtualSlot vSlot in vCon.Scheduler.Slots.List())
                {
                    vSlot.Status = SlotStatus.Paused;
                }
            }

            return;
        }

        private void Process(NNTPCommands zCommand, VirtualConnection vCon)
        {
            string sError = "";

            try
            {
                VirtualSlot vSlot;
                //vCon.Server.WriteStatus("Decoding #" + zCommand.ID);

                if ((zCommand.Status == WorkStatus.Failed) || (zCommand.Status == WorkStatus.Missing))
                {
                    zCommand.Data = null;
                    zCommand.Segment.LogError(zCommand.ID, zCommand.Error);
                    //vCon.Scheduler.Slots.Log.Enqueue(zCommand.Error.Message);
                }
                //else
                //{
                //    DecodeArticle(zCommand);
                //}

                if (zCommand.Segment.Output.Store(zCommand.ID, zCommand)) 
                {
                    if (zCommand.Segment.Output.Finished) 
                    {
                        zCommand.Segment.IsDecoded = true;

                        vSlot = vCon.Scheduler.Slots.Item(zCommand.Segment.SlotID);

                        if ((vSlot != null) && (vSlot.IsDecoded))
                        {
                            bool bCompleted = false;
                            List<string> cList = new List<string>();

                            foreach (VirtualFile vFile in vSlot.List())
                            {
                                if (vFile.Output.Data.Length > 0)
                                {
                                    bCompleted = true;
                                    break;
                                }
                                else
                                {
                                    cList.Add(Common.MostFrequent(vFile.Errors.GetEnumerator()));
                                }
                            }

                            if (bCompleted)
                            {
                                vSlot.Status = SlotStatus.Completed;
                            }
                            else
                            {
                                vSlot.StatusLine = Common.MostFrequent(cList.GetEnumerator());
                                if (vSlot.StatusLine.Length == 0) { vSlot.StatusLine = "No data"; }
                                vSlot.Status = SlotStatus.Failed;
                            }
                        }
                    }
                    return; 
                }
                throw new Exception("Store #" + zCommand.ID);
            }
            catch (Exception ex)
            {
                sError = "Decode: " + ex.Message;
                try { if (!vCon.Cancelled) { vCon.Server.WriteStatus(sError); } } catch {} 
            }
        }

        private bool DecodeArticle(NNTPCommands zCommand)
        {
            int zTotal = 0;
            string zLine = null;

            ArticleDecoder decoder = null;
            StreamReader sr = Common.GetReader((MemoryStream)zCommand.Data);

            while (!sr.EndOfStream)
            {
                zLine = sr.ReadLine();
                zTotal += zLine.Length + 2;
                if (zLine.Length == 0) { break; }
            }

            if (!sr.EndOfStream)
            {
                zLine = sr.ReadLine();

                int zLength = 0;

                switch(zLine.Split(' ')[0].ToLower()) 
                {
                    // Determine encoding

                    case "=ybegin":

                    //    decoder = new yEnc();
                    //    yEnc yDecoder = (yEnc)decoder;

                    //    zTotal += zLine + "  ";
                    //    zCommand.File = yDecoder.DecodeHeader(zLine);

                    //    if (zCommand.File == null) { return false; }

                    //    string zPart = sr.ReadLine();
                    //    zTotal += zPart + "  ";

                    //    zCommand.Part = yDecoder.DecodePart(zPart);
                    //    if (zCommand.Part == null) { return false; }

                    //    zLength = (zCommand.Part.End - zCommand.Part.Begin) + 1;
                    //    zCommand.Data.Position = zTotal;

                    //    break;

                    default:

                        decoder = new Plain();

                        zCommand.Data.Position = zTotal;
                        zLength = (int)(zCommand.Data.Length - zCommand.Data.Position);

                        break;
                }
                
                zCommand.Data = decoder.DecodeBytes(zCommand.Data, zLength);
                return true;
            }

            return false;
        }

        private static bool HandleError(NNTPCommands zCommand, VirtualConnection vConnection)
        {
            zCommand.LogError(zCommand.Error, vConnection);

            switch (zCommand.Error.Code)
            {
                case (int)NNTPCodes.DoNotTryAgain:
                case (int)NNTPCodes.TooManyConnections:
                case (int)NNTPCodes.GoodBye:

                    zCommand.Status = WorkStatus.Failed;
                    return false;

                case (int)NNTPCodes.IDNotFound: 
                case (int)NNTPCodes.NumberNotFound: 

                    zCommand.Status = WorkStatus.Missing;
                    zCommand.Error.Tries += 1;
                    return true;

                default:

                    zCommand.Status = WorkStatus.Failed;
                    zCommand.Error.Tries += 1;
                    return true;
            }

        }

        internal Task Task(VirtualConnection vConnection)
        {
            Action aMain = (Action)(() => Main(vConnection));
            return new Task(aMain, vConnection.Token.Token, TaskCreationOptions.LongRunning);
        }

        //private Task Processing(NNTPCommands zCommand, VirtualConnection vConnection)
        //{
        //    Action dMain = (Action)(() => Process(zCommand, vConnection));
        //    return new Task(dMain, vConnection.Token.Token, TaskCreationOptions.None);
        //}
    }

}  // <WdmCfM0ay7g>
