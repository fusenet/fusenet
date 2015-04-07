using System;
using System.IO;
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
    internal interface ArticleDecoder
    {
        Stream DecodeBytes(Stream Data, int Length);
    }

    internal class Plain : ArticleDecoder
    {

        public Stream DecodeBytes(Stream Data, int Length)
        {
            if (Data == null) { return null; }

            int zPos = 0;
            int iPos = 0;
            bool SkipNext = false;

            byte[] zOut = new byte[Length];
            byte[] zData = new byte[Data.Length];

            Data.Read(zData, 0, zData.Length);

            foreach (byte b in zData)
            {
                iPos++;

                if (SkipNext)
                {
                    SkipNext = false;
                    continue;
                }

                if ((b == 10) && (zData[iPos] == 46))
                {
                    SkipNext = true; // Remove double dots
                }

                zOut[zPos] = b;
                zPos++;

                if (zPos >= (Length)) { break; }
            }

            return new MemoryStream(zOut);
        }
    }

    internal class yEnc : ArticleDecoder
	{
        internal PartInfo DecodePart(string sLine)
        {
            int zEnd = 0;
            int zBegin = 0;

            foreach(string s in sLine.Split(' '))
            {
                if (s.StartsWith("begin"))
                {
                    zBegin = int.Parse(s.Remove(0, 6));
                }
                if (s.StartsWith("end"))
                {
                    zEnd = int.Parse(s.Remove(0, 4));
                }
            }

            return new PartInfo(zBegin, zEnd);
        }

        internal FileInfo DecodeHeader(string sLine)
        {
            int c = sLine.IndexOf("name");

            string name = sLine.Substring(c + 5);
            string ybegin = sLine.Substring(0, c - 1);

            sLine = ybegin.Replace(" ", "");
            ybegin = sLine.Replace("part=", " part=");
            sLine = ybegin.Replace("line=", " line=");
            ybegin = sLine.Replace("size=", " size=");
            sLine = ybegin + " name=" + name;

            int b = sLine.IndexOf("size=");
            int e = sLine.IndexOf(" ", b);

            int fSize = int.Parse(sLine.Substring(b + 5, e - b - 5));
            string fName = sLine.Substring(sLine.IndexOf("name=") + 5);

            return new FileInfo(fName, fSize);          
        }

        public Stream DecodeBytes(Stream Data, int Length)
        {
            if (Data == null) { return null; }

            int zPos = 0;
            int iPos = 0;

            bool bEscaped = false;

            byte[] zOut = new byte[Length];
            byte[] zData = new byte[Data.Length];

            Data.Read(zData, 0, zData.Length);

            foreach(byte b in zData)
            {
                iPos++;

                switch (b)
                {
                    case 10:

                        if (zData[iPos] == 46)
                        {
                            zData[iPos] = 13; // Remove double dots
                        }

                        continue;

                    case 13:
                        continue;

                    case 61:
                        bEscaped = true;
                        continue;
                }

                byte bOut = b;

                unchecked
                {
                    if (bEscaped) 
                    { 
                        bOut -= 64;
                        bEscaped = false;
                    }

                    bOut -= 42;
                }

                zOut[zPos] = bOut;
                
                zPos++;

                if (zPos >= Length) { break; }
            }

            return new MemoryStream(zOut);
        }
	}
} // <wQE7DrrPDbU>
