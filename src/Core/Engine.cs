using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Collections;
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

using Fusenet.API;
using Fusenet.Utils;

namespace Phuse
{
    public class Engine
    {
        //private Webserver Server;
        private Scheduler Scheduler = new Scheduler();

        ~Engine()
        {
            Close();
        }

        public SlotList Slots
        {
            get { lock (Scheduler) { return new SlotList(Scheduler); } }
        }

        public ServerList Servers
        {
            get { lock (Scheduler) { return new ServerList(Scheduler); } }
        }

        public bool Close()
        {
            if (Scheduler == null) { return true; }

            lock (Scheduler)
            {
                bool bVal = false;

                if (Scheduler != null)
                {
                    bVal = Scheduler.Close();
                    Scheduler = null;
                }

                //if (Server != null)
                //{
                //    Server.Close();
                //    Server = null;
                //}

                return bVal;
            }
        }

        //public string URL
        //{
        //    get 
        //    {
        //      lock (Scheduler)
        //      {
        //        if (Server == null)
        //        {
        //            Server = new Webserver();

        //            Server.Port = 112;
        //            Server.Custom = new WebHandler(this);
        //            Server.Start();
        //        }
        //        return Server.URL + Server.Custom.VirtualDirectory; 
        //      }
        //    }
        //}

        public string XML
        {
            get
            {
                lock (Scheduler)
                {
                    if ((Scheduler != null) && (Scheduler.Slots != null))
                    {
                        return Scheduler.Slots.XML;
                    }
                    return "";
                }
            }
        }
    }
} // <Qhh-MJVWwXY>
