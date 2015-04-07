using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Xml;
using System.Threading;

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
    static class NZB
    {
        internal static List<NNTPInput> Parse(Stream xXML)
        {
            NNTPInput nI = null;
            List<NNTPInput> cList = new List<NNTPInput>();

            try
            {
                XmlReader xR = XmlReader.Create(xXML, Common.ReaderSettings);

                while (xR.ReadToFollowing("file"))
                {
                    nI = ParseSegments(xR.ReadSubtree(), xR.GetAttribute("subject"));
                    if ((nI != null) && (nI.Segments.Count > 0)) { cList.Add(nI); }
                }
            }

            catch { return null; }

            if (cList.Count == 0) { return null; } else { return cList; } 
        }

        private static NNTPInput ParseSegments(XmlReader sR, string Subject)
        {
            NNTPInput nI = null;

            try
            {
                nI = new NNTPInput(Subject);

                while (sR.ReadToFollowing("segment"))
                {
                    int lBytes = 0;
                    int lNumber = 0;

                    while (sR.MoveToNextAttribute())
                    {
                        switch (sR.Name.ToLower())
                        {
                            case "bytes":
                                lBytes = sR.ReadContentAsInt();
                                break;

                            case "number":
                                lNumber = sR.ReadContentAsInt();
                                break;
                        }
                    }

                    sR.MoveToContent();
                    sR.Read();

                    string sMsgID = sR.Value;
                    if (sMsgID.Length < 1) { return null; }

                    if ((lNumber > 0) && (lBytes > 0))
                    {
                        nI.Segments.Add(new NNTPSegment(lNumber, lBytes, sMsgID));
                    }
                }
            }

            catch { return null; }

            if ((nI != null) && (nI.Segments.Count > 0)) { return nI; }

            return null; 
        }

    }
} // <HnwsSCCY5t>
