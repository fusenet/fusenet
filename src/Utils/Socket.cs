using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

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

namespace Fusenet.Utils
{
    internal interface VirtualSocket
    {
        bool Receive();
        bool IsConnected();
        
        bool Send(Stream bData);
        bool Send(Stream bData, int ExpectedBytesReturned);

        bool Close(int iCode, string sError);
        bool Connect(VirtualServer SVR);

        event EventHandler<WorkArgs> Received;
        event EventHandler<WorkArgs> Connected;
        event EventHandler<WorkArgs> Disconnected;
    }

    internal class Socket : VirtualSocket
	{
        private Stream DataStream;
		private bool CancelSocket = false;
		private int MaxBufferSize = 100000;
        private System.Net.Sockets.Socket ClientSocket;

        private SocketAsyncEventArgs iSend;
        private SocketAsyncEventArgs iConnect;
        private SocketAsyncEventArgs iReceive;

        public event EventHandler<WorkArgs> Received;
        public event EventHandler<WorkArgs> Connected;
        public event EventHandler<WorkArgs> Disconnected;
        
        public bool Close(int iCode, string sError)
		{
            Clear();
            SafeFire(Disconnected, new WorkArgs(iCode, sError));

            return true;
        }

        private void Clear()
        {
            CancelSocket = true;

            iSend = null;
            iReceive = null;
            iConnect = null;

            if (ClientSocket != null)
            {
                try { if (ClientSocket.Connected) { ClientSocket.Shutdown(SocketShutdown.Both); } }
                catch { }

                try { ClientSocket.Close(); }
                catch { }

                ClientSocket = null;
            }

            ClearStream();
        }

		public bool Connect(VirtualServer SVR)
		{
			try
			{

                Clear();

                CancelSocket = false;

				ClientSocket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
				iConnect = new System.Net.Sockets.SocketAsyncEventArgs();

                iConnect.Completed += new EventHandler<SocketAsyncEventArgs>(iConnect_Completed);
                iConnect.RemoteEndPoint = new DnsEndPoint(SVR.Host, SVR.Port); //.SocketClientAccessPolicyProtocol = TCP

				if (!ClientSocket.ConnectAsync(iConnect))
				{
					iConnect_Completed(null, iConnect); // Synchronously
				}

                return true;

			}
            catch (Exception ex) { Close(952, "Socket_Connect: " + ex.Message); return false; }
		}

		private void iConnect_Completed(object sender, System.Net.Sockets.SocketAsyncEventArgs e)
		{
            try
            {
                if (CancelSocket) { return; }

                if (e.SocketError != SocketError.Success)
                {
                    NNTPError zErr = Common.TranslateError(e.SocketError);
                    Close(zErr.Code, zErr.Message);
                    return;
                }

                iReceive = new System.Net.Sockets.SocketAsyncEventArgs();
                iReceive.Completed += new EventHandler<SocketAsyncEventArgs>(iReceive_Completed);

                if (MaxBufferSize < 1024) { MaxBufferSize = 1024; }

                ClearBuffer();
                SafeFire(Connected, new WorkArgs(951, "Connected"));

            }
            catch (Exception ex) { Close(950, "Connect: " + ex.Message); }
		}

		public bool Receive()
		{
			try
			{
                if (iReceive == null) { return false; }

				if (!ClientSocket.ReceiveAsync(iReceive))
				{
					iReceive_Completed(null, iReceive);
				}

                return true;

			}
            catch (Exception ex) { Close(952, "ReceiveAsync: " + ex.Message); return false; }
		}

        private void ClearStream()
        {
            DataStream = null;
        }

        public bool IsConnected()
        {
            if (ClientSocket == null) { return false; }
            return ClientSocket.Connected;
        }

		private void ClearBuffer(int InitialSize = -1)
		{
			try
			{
                ClearStream();

                if (iReceive != null) 
                {
                    if (InitialSize < (MaxBufferSize / 100))
                    {
                        SetBuffer(MaxBufferSize / 100);
                        return;
                    }                   

                    if (InitialSize < MaxBufferSize)
                    {
                        SetBuffer(InitialSize);
                        return;
                    }
                    
                    SetBuffer(MaxBufferSize);
                }
			}
			catch (Exception ex) { Close(953, "Clear: " + ex.Message); }
		}

		private void iReceive_Completed(object sender, System.Net.Sockets.SocketAsyncEventArgs e)
		{

			if (CancelSocket)  { return; }

			try
			{
				if (e.SocketError != SocketError.Success)
				{
                    NNTPError zErr = Common.TranslateError(e.SocketError);
					Close(zErr.Code, zErr.Message);
					return;
				}

                if (e.BytesTransferred < 1)
                {
                    Close(954, "Socket closed.");
                    return;
                }

                if (DataStream == null)
                {
                    DataStream = new MemoryStream(e.BytesTransferred + 1);
                }

                DataStream.Position = DataStream.Length;
                DataStream.Write(e.Buffer, e.Offset, e.BytesTransferred);

				if (e.BytesTransferred >= (e.Buffer.Length / 2.0)) // Dynamic resize
				{
                    if ((e.Buffer.Length - 1) < (MaxBufferSize - 5))
                    {
                        int bsize = 0;

					    if ((e.Buffer.Length * 2) < MaxBufferSize)
					    { bsize = e.Buffer.Length * 2 + 1; }
					    else
					    { bsize = MaxBufferSize + 1; }

                        SetBuffer(bsize);
                    }
				}

                SafeFire(Received, new WorkArgs(DataStream));

            }
			catch (Exception ex) { Close(960, "Rcv: " + ex.Message); }
		}

        public bool Send(Stream bData)
        {
            ClearBuffer();
            return InternalSend(bData);
        }

        public bool Send(Stream bData, int ExpectedBytesReturned)
        {
            ClearBuffer(ExpectedBytesReturned);
            return InternalSend(bData);
        }

        private bool InternalSend(Stream bData)
		{
			try
			{
                iSend = new System.Net.Sockets.SocketAsyncEventArgs();
                iSend.Completed += new EventHandler<SocketAsyncEventArgs>(iSend_Completed);

                byte[] bD = Common.GetBytes(bData);
				iSend.SetBuffer(bD, 0, bD.Length);

				if (!ClientSocket.SendAsync(iSend)) { iSend_Completed(null, iSend); }
                return true;

			}
            catch (Exception ex) { Close(970, "Send: " + ex.Message); return false; }
		}

        private void SetBuffer(int ReceiveBufferSize)
        {
            if (iReceive != null)
            {
                if ((iReceive.Buffer == null) || (iReceive.Buffer.Length < ReceiveBufferSize))
                {
                    byte[] TempBuffer = new byte[ReceiveBufferSize];
                    iReceive.SetBuffer(TempBuffer, 0, TempBuffer.Length); // Initial size
                }
            }
        }

		private void iSend_Completed(object sender, System.Net.Sockets.SocketAsyncEventArgs e)
		{
			if (CancelSocket) {	return;	}

			try
			{
				if (e.SocketError != SocketError.Success)
				{
                    NNTPError zErr = Common.TranslateError(e.SocketError);
					Close(zErr.Code, zErr.Message);
					return;
				}
			}
			catch (Exception ex) { Close(971, "Send: " + ex.Message); }
    	}

        private void SafeFire(EventHandler<WorkArgs> Ev, WorkArgs Args)
        {
            EventHandler<WorkArgs> tmp = Ev;
            if (tmp != null) { tmp(this, Args); }
        }

    }

    internal class SSLSocket : VirtualSocket
    {
        private byte[] RcvBuffer;
        private Stream DataStream;
        private int MaxBufferSize = 100000;
        private bool CancelSocket = false;

        private string LastHost;
        private Stream SocketStream;
        private TcpClient SocketClient;

        public event EventHandler<WorkArgs> Received;
        public event EventHandler<WorkArgs> Connected;
        public event EventHandler<WorkArgs> Disconnected;

        public bool Close(int iCode, string sError)
        {
            Clear();

            SafeFire(Disconnected, new WorkArgs(iCode, sError));

            return true;
        }

        private void Clear()
        {
            CancelSocket = true;

            if (SocketStream != null)
            {
                try { SocketStream.Close(); }
                catch { }

                SocketStream = null;
            }

            if (SocketClient != null)
            {
                try { SocketClient.Close(); }
                catch { }

                SocketClient = null;
            }

            ClearStream();
        }

        public bool Connect(VirtualServer SVR)
        {
            try
            {

                Clear();

                CancelSocket = false;

                SocketStream = null;
                SocketClient = new TcpClient();

                LastHost = SVR.Host;
                IAsyncResult r = SocketClient.BeginConnect(SVR.Host, SVR.Port, new AsyncCallback(iConnect_Completed), SocketClient);

                return true;

            }
            catch (Exception ex) { Close(952, "Socket_Connect: " + ex.Message); return false; }
        }

        private static bool ValidateRemoteCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors policyErrors)
        {
            return true;
        }

        private void iConnect_Completed(IAsyncResult e)
        {

            try
            {
                if (CancelSocket) { return; }

                if (e == null) { return; }
                if (e.CompletedSynchronously) { return; }
                if (e.AsyncState != SocketClient) { return; }

                SocketClient.EndConnect(e);

                SslStream _s = new SslStream(SocketClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateRemoteCertificate), null, EncryptionPolicy.AllowNoEncryption);

                SocketStream = _s;

                _s.AuthenticateAsClient(LastHost, new X509CertificateCollection(), System.Security.Authentication.SslProtocols.Default, false);
                iAuth_Completed();

                return;

            }
            catch (Exception ex) { Close(950, "Connect: " + ex.Message); }
        }

        private void iAuth_Completed()
        {
            try
            {
                if (CancelSocket) { return; }

                SslStream _s = (SslStream)SocketStream;

                if (MaxBufferSize < 1024) { MaxBufferSize = 1024; }

                ClearBuffer();
                SafeFire(Connected, new WorkArgs(951, "Connected"));

            }
            catch { Close(955, "Server doesn't support SSL."); }
        }

        public bool Receive()
        {
            try
            {
                iReceive_Completed(SocketStream.Read(RcvBuffer, 0, RcvBuffer.Length));
                
                return true;

            }
            catch (Exception ex) { Close(952, "ReceiveAsync: " + ex.Message); return false; }
        }

        private void ClearBuffer(int InitialSize = -1)
        {
            try
            {
                ClearStream();

                if (InitialSize < (MaxBufferSize / 100))
                {
                    SetBuffer(MaxBufferSize / 100);
                    return;
                }

                if (InitialSize < MaxBufferSize)
                {
                    SetBuffer(InitialSize);
                    return;
                }

                SetBuffer(MaxBufferSize);
            }
            catch (Exception ex) { Close(953, "Clear: " + ex.Message); }
        }

        private void ClearStream()
        {
            DataStream = null;
        }

        public bool IsConnected()
        {
            if (SocketClient == null) { return false; }
            return SocketClient.Connected;
        }

        private void iReceive_Completed(int BytesTransferred)
        {
            if (CancelSocket) { return; }

            try
            {
                if (BytesTransferred < 1)
                {
                    Close(954, "Socket closed.");
                    return;
                }

                if (DataStream == null)
                {
                    DataStream = new MemoryStream(BytesTransferred + 1);
                }

                DataStream.Position = DataStream.Length;
                DataStream.Write(RcvBuffer, 0, BytesTransferred);

                if (BytesTransferred >= (RcvBuffer.Length / 2.0)) // Dynamic resize
                {
                    if ((RcvBuffer.Length - 1) < (MaxBufferSize - 5))
                    {
                        int bsize = 0;

                        if ((RcvBuffer.Length * 2) < MaxBufferSize)
                        { bsize = RcvBuffer.Length * 2 + 1; }
                        else
                        { bsize = MaxBufferSize + 1; }

                        SetBuffer(bsize);
                    }
                }

                SafeFire(Received, new WorkArgs(DataStream));

            }
            catch (Exception ex) { Close(960, "Rcv: " + ex.Message); }
        }

        public bool Send(Stream bData)
        {
            ClearBuffer();
            return InternalSend(bData);
        }

        public bool Send(Stream bData, int ExpectedBytesReturned)
        {
            ClearBuffer(ExpectedBytesReturned);
            return InternalSend(bData);
        }

        private bool InternalSend(Stream bData)
        {
            try
            {
                byte[] bD = Common.GetBytes(bData);
                SocketStream.Write(bD, 0, bD.Length);

                return true;

            }
            catch (Exception ex) { Close(970, "Send: " + ex.Message); return false; }
        }

        private void SetBuffer(int ReceiveBufferSize)
        {
            if ((RcvBuffer == null) || (RcvBuffer.Length < ReceiveBufferSize))
            {
                RcvBuffer = new byte[ReceiveBufferSize];
            }
        }

        private void SafeFire(EventHandler<WorkArgs> Ev, WorkArgs Args)
        {
            EventHandler<WorkArgs> tmp = Ev;
            if (tmp != null) { tmp(this, Args); }
        }
    }

} // <6DEBJyon8JM>