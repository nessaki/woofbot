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
using System.Text;
using jabber;
using jabber.client;
using jabber.connection;

namespace Jarilo
{
    public class XmppBot: IDisposable, IRelay
    {
        Program MainProgram;
        public XmppServerInfo Conf;
        Configuration MainConf;
        public JabberClient Client;
        public StanzaStream Stream;

        public XmppBot(Program p, XmppServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;

        }

        public void Dispose()
        {
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
            Client.User = Conf.User;
            Client.Server = Conf.Domain;
            Client.NetworkHost = Conf.NetworkHost;
            Client.Password = Conf.Password;
            Client.Connect();
        }

        void Client_OnConnect(object sender, StanzaStream stream)
        {
            Stream = stream;
            Console.WriteLine("XMPP connected to " + Client.Server);
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
