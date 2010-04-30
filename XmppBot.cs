﻿// 
// Jarilo
// Copyright (c) 2010, Jarilo Development Team
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Jarilo", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id$
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using jabber;
using jabber.client;
using jabber.connection;

namespace Jarilo
{
    public class XmppBot : IDisposable, IRelay
    {
        Program MainProgram;
        public XmppServerInfo Conf;
        Configuration MainConf;
        public JabberClient Client;
        public StanzaStream Stream;
        public ConferenceManager ConfManager;
        public List<Room> Conferences = new List<Room>();

        public XmppBot(Program p, XmppServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;

        }

        public void Dispose()
        {
            Conferences.Clear();

            if (ConfManager != null)
            {
                ConfManager.Dispose();
                ConfManager = null;
            }

            if (Stream != null)
            {
                if (Stream.Connected)
                    Stream.Close(false);
                Stream = null;
            }

            if (Client != null)
            {
                Client.Dispose();
            }
            Client = null;
        }

        public void Connect()
        {
            Conferences.Clear();

            if (ConfManager != null)
            {
                ConfManager.Dispose();
                ConfManager = null;
            }

            if (Stream != null)
            {
                if (Stream.Connected)
                    Stream.Close(false);
                Stream = null;
            }

            if (Client != null)
                Client.Dispose();

            Client = new JabberClient();
            Client.OnInvalidCertificate += new System.Net.Security.RemoteCertificateValidationCallback(Client_OnInvalidCertificate);
            Client.OnConnect += new jabber.connection.StanzaStreamHandler(Client_OnConnect);
            Client.OnReadText += new bedrock.TextHandler(Client_OnReadText);
            Client.OnWriteText += new bedrock.TextHandler(Client_OnWriteText);
            Client.OnError += new bedrock.ExceptionHandler(Client_OnError);
            Client.OnStreamError += new jabber.protocol.ProtocolHandler(Client_OnStreamError);
            Client.OnAuthenticate += new bedrock.ObjectHandler(Client_OnAuthenticate);
            Client.OnAuthError += new jabber.protocol.ProtocolHandler(Client_OnAuthError);
            ConfManager = new ConferenceManager();
            ConfManager.Stream = Client;

            Client.User = Conf.User;
            Client.Server = Conf.Domain;
            Client.NetworkHost = Conf.NetworkHost;
            Client.Password = Conf.Password;
            Client.KeepAlive = 30f;
            Client.AutoLogin = true;
            Client.AutoStartTLS = true;
            Client.AutoReconnect = 15f;
            Client.AutoPresence = true;
            Client.AutoIQErrors = true;
            Client.Connect();
        }

        void Client_OnAuthError(object sender, System.Xml.XmlElement rp)
        {
            Console.WriteLine("Error logging into XMPP server: {0}", rp.ToString());
        }

        void Client_OnAuthenticate(object sender)
        {
            ThreadPool.QueueUserWorkItem(sync =>
                {
                    Console.WriteLine("XMPP logged in to " + Client.NetworkHost);
                    foreach (string addr in Conf.Conferences.Values)
                    {
                        try
                        {
                            Room room = ConfManager.GetRoom(addr + "/" + Conf.User);
                            room.OnJoin += new RoomEvent(room_OnJoin);
                            room.OnLeave += new RoomPresenceHandler(room_OnLeave);
                            room.Join();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error joining conference {0}: {1}", addr, ex.Message);
                        }
                    }
                }
            );
        }

        void room_OnLeave(Room room, jabber.protocol.client.Presence pres)
        {
            if (Conferences.Contains(room))
                Conferences.Remove(room);
        }

        void room_OnJoin(Room room)
        {
            Console.WriteLine("Joined group chat " + room.RoomAndNick.ToString());
            room.OnRoomMessage += new MessageHandler(room_OnRoomMessage);

            Conferences.Add(room);
        }

        void room_OnRoomMessage(object sender, jabber.protocol.client.Message msg)
        {
            System.Console.WriteLine("{0}: {1}", msg.From.Resource, msg.Body);
        }

        private void Client_OnReadText(object sender, string txt)
        {
            if (txt != " ")
                Console.WriteLine("RECV: " + txt);
        }

        private void Client_OnWriteText(object sender, string txt)
        {
            return;
            if (txt != " ")
                Console.WriteLine("SENT: " + txt);
        }

        private void Client_OnError(object sender, Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.ToString());
        }


        private void Client_OnStreamError(object sender, System.Xml.XmlElement rp)
        {
            Console.WriteLine("Stream ERROR: " + rp.OuterXml);
        }

        void Client_OnConnect(object sender, StanzaStream stream)
        {
            Console.WriteLine("XMPP server connected, logging in...");
        }

        bool Client_OnInvalidCertificate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public void RelayMessage(BridgeInfo bridge, string from, string message)
        {
        }

    }
}