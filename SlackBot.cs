// BSD 3-Clause License
//
// Copyright (c) 2015-2019, Alchemy Development Group
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
//     * Redistributions of source code must retain the above copyright notice, this
//       list of conditions and the following disclaimer.
//
//     * Redistributions in binary form must reproduce the above copyright notice,
//       this list of conditions and the following disclaimer in the documentation
//       and/or other materials provided with the distribution.
//
//     * Neither the name of the copyright holder, nor the names of its
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

#if SLACK
using System;
using System.Linq;
using SlackAPI;
using OpenMetaverse;

namespace WoofBot
{
    public class SlackServerInfo : AChanStringsInfo
    {
        public string APIKEY;
    }

    public class SlackBot : IRelay
    {
        Program MainProgram;
        public SlackServerInfo Conf;
        public AInfo GetConf() => Conf;
        Configuration MainConf;
        SlackSocketClient Client;

        public SlackBot(Program p, SlackServerInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
        }

        public void Connect()
        {
            Client = new SlackSocketClient(Conf.APIKEY);
            Client.Connect(o => { });
            Client.OnMessageReceived += Client_OnMessageReceived;
        }

        private void Client_OnMessageReceived(SlackAPI.WebSocketMessages.NewMessage msg)
        {
            if (msg.ok && msg.subtype != "bot_message" && Conf.Channels.Any(x => x.Value == Client.ChannelLookup[msg.channel].name))
            {
                string channel_name = Client.ChannelLookup[msg.channel].name;
                string from = $"(slack:{channel_name}) {Client.UserLookup[msg.user].name}";
                Console.WriteLine($"{from}: {msg.text}");
                MainProgram.RelayMessage(Program.EBridgeType.SLACK,
                    b => b.SlackServerConf == Conf && Conf.Channels[b.SlackChannelID] == channel_name.ToLower(),
                    $from, msg.text);
            }
        }

        public bool IsConnected() => Client != null && Client.IsConnected;

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            if (Client != null)
            {
                if (Conf.Channels.TryGetValue(bridge.SlackChannelID, out string confChan))
                {
                    var channel = Client.ChannelLookup.First(x => x.Value.name == confChan).Value;
                    if (channel != null)
                        Client.PostMessage(o => { if (!o.ok) Console.WriteLine("Slack - Failed to send message: " + o.error); }, channel.id, msg, from);
                }
            }
        }
    }
}
#endif
