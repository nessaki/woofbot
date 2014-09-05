// 
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
using System.Threading;
using jabber.client;
using jabber.connection;
using OpenMetaverse;

namespace BarkBot
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Room conf in Conferences)
                {
                    if (conf.IsParticipating)
                        conf.Leave("client quit");
                }
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
        }

        public void Connect()
        {
            Client = new JabberClient();
            Client.Resource = "BarkBot";
            Client.OnInvalidCertificate += new System.Net.Security.RemoteCertificateValidationCallback(Client_OnInvalidCertificate);
            Client.OnConnect += new jabber.connection.StanzaStreamHandler(Client_OnConnect);
#if DEBUG_XMPP
            Client.OnReadText += new bedrock.TextHandler(Client_OnReadText);
            Client.OnWriteText += new bedrock.TextHandler(Client_OnWriteText);
#endif
            Client.OnError += new bedrock.ExceptionHandler(Client_OnError);
            Client.OnStreamError += new jabber.protocol.ProtocolHandler(Client_OnStreamError);
            Client.OnAuthenticate += new bedrock.ObjectHandler(Client_OnAuthenticate);
            Client.OnAuthError += new jabber.protocol.ProtocolHandler(Client_OnAuthError);
            Client.OnDisconnect += new bedrock.ObjectHandler(Client_OnDisconnect);
            ConfManager = new ConferenceManager();
            ConfManager.Stream = Client;

            Client.User = Conf.User;
            Client.Server = Conf.Domain;
            Client.NetworkHost = Conf.NetworkHost;
            Client.Password = Conf.Password;
            Client.Resource = "bot";
            Client.KeepAlive = 30f;
            Client.AutoLogin = true;
            Client.AutoStartTLS = true;
            Client.AutoReconnect = 15f;
            Client.AutoPresence = true;
            Client.AutoIQErrors = true;
            Client.Connect();
        }

        public bool IsConnected
        {
            get { return Conferences.Any(conf => conf.IsParticipating); }
        }

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            try
            {
                if (Client != null)
                {
                    string confID;
                    Room room = null;
                    if (Conf.Conferences.TryGetValue(bridge.XmppConferenceID, out confID))
                    {
                        room = Conferences.Find((Room r) => { return r.JID.ToString() == confID; });
                    }
                    if (room != null && room.IsParticipating)
                    {
                        if (msg.StartsWith("/me "))
                            room.PublicMessage(string.Format("{0}{1}", from, msg.Substring(3)));
                        else
                            room.PublicMessage(string.Format("{0}: {1}", from, msg));
                    }
                    else if (room != null)
                    {
                        room.Join();
                    }
                    else
                    {
                        try
                        {
                            room = ConfManager.GetRoom(bridge.XmppServerConf.Conferences[bridge.XmppConferenceID] + "/" + Conf.Nick);
                            room.OnJoin += new RoomEvent(room_OnJoin);
                            room.OnLeave += new RoomPresenceHandler(room_OnLeave);
                            room.OnPresenceError += new RoomPresenceHandler(room_OnPresenceError);
                            room.Join();
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("XMPP - Failed to send message: " + ex.Message);
            }
        }

        void Client_OnAuthError(object sender, System.Xml.XmlElement rp)
        {
            Console.WriteLine("Error logging into XMPP server: {0}", rp.ToString());
        }

        void Client_OnAuthenticate(object sender)
        {
            Console.WriteLine("XMPP logged in to " + Client.NetworkHost);
            foreach (string addr in Conf.Conferences.Values)
            {
                try
                {
                    Room room = ConfManager.GetRoom(addr + "/" + Conf.Nick);
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

        void room_OnLeave(Room room, jabber.protocol.client.Presence pres)
        {
            Console.WriteLine("Left group chat " + room.RoomAndNick.ToString());
            if (Client != null)
            {
                if (Client.IsAuthenticated)
                {
                    var id = room.RoomAndNick;

                    ThreadPool.QueueUserWorkItem(sync =>
                    {
                        Thread.Sleep(10*1000);
                        if (!Client.IsAuthenticated) return;
                        try
                        {
                            Room nroom = ConfManager.GetRoom(id);
                            nroom.OnJoin += new RoomEvent(room_OnJoin);
                            nroom.OnLeave += new RoomPresenceHandler(room_OnLeave);
                            nroom.Join();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error joining conference {0}: {1}", id, ex.Message);
                        }
                    });
                }
            }

            lock (Conferences)
            {
                if (Conferences.Contains(room))
                    Conferences.Remove(room);
            }
        }

        int joinTime = 0;

        void room_OnJoin(Room room)
        {
            Console.WriteLine("Joined group chat " + room.RoomAndNick.ToString());
            room.OnRoomMessage += new MessageHandler(room_OnRoomMessage);
            joinTime = Environment.TickCount;
            lock (Conferences) Conferences.Add(room);
        }

        void room_OnPresenceError(Room room, jabber.protocol.client.Presence pres)
        {
            Logger.DebugLog("room_OnPresenceError: " + pres.ToString());
        }

        void room_OnRoomMessage(object sender, jabber.protocol.client.Message msg)
        {
            Room conf = (Room)sender;
            System.Console.WriteLine("(xmpp:{0}) {1}: {2}", conf.JID.User, msg.From.Resource, msg.Body);

            if (Environment.TickCount - joinTime < 5000) return;

            try
            {
                List<BridgeInfo> bridges = MainConf.Bridges.FindAll((BridgeInfo b) =>
                {
                    return
                        b.XmppServerConf == Conf &&
                        Conf.Conferences[b.XmppConferenceID] == conf.JID.ToString();
                });

                foreach (BridgeInfo bridge in bridges)
                {
                    if (bridge.Bot != null && bridge.GridGroup != UUID.Zero)
                    {
                        GridBot bot = MainProgram.GridBots.Find((GridBot b) => { return b.Conf == bridge.Bot; });
                        if (bot != null)
                        {
                            bot.RelayMessage(bridge,
                                string.Format("(xmpp:{0}) {1}", bridge.XmppConferenceID, msg.From.Resource),
                                msg.Body);
                        }
                    }

                    if (bridge.IrcServerConf != null)
                    {
                        IrcBot bot = MainProgram.IrcBots.Find((IrcBot b) => { return b.Conf == bridge.IrcServerConf; });
                        if (bot != null)
                        {
                            bot.RelayMessage(bridge,
                                string.Format("(xmpp:{0}) {1}", bridge.XmppConferenceID, msg.From.Resource),
                                msg.Body);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed relaying message: {0}", ex.Message);
            }
        }

#if DEBUG_XMPP
        private void Client_OnReadText(object sender, string txt)
        {
            if (txt != " ")
                Console.WriteLine("RECV: " + txt);
        }

        private void Client_OnWriteText(object sender, string txt)
        {
            if (txt != " ")
                Console.WriteLine("SENT: " + txt);
        }
#endif

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

        void Client_OnDisconnect(object sender)
        {
            Console.WriteLine("XMPP server disconnected.");
        }

        bool Client_OnInvalidCertificate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}
