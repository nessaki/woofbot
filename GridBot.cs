// BSD 3-Clause License
// 
// Copyright (c) 2014-2019, Alchemy Development Group
// Copyright (c) 2010, Jarilo Development Team
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

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OpenMetaverse;
using Nini.Config;

namespace WoofBot
{
    public class BotInfo : AInfo
    {
        public string FirstName;
        public string LastName;
        public string Password;
        public UUID SitOn;
        internal ulong SimHandle;
        public string Sim;
        public Vector3 PosInSim;
        public string LoginURI = "https://login.agni.lindenlab.com/cgi-bin/login.cgi";
        public string GridName = "agni";
        public string Name => $"{FirstName} {LastName}";

        public static BotInfo Create(IniConfig conf, string id)
        {
            var b = new BotInfo()
            {
                ID = id,
                FirstName = conf.Get("first_name"),
                LastName = conf.Get("last_name"),
                Password = conf.Get("password"),
                SitOn = (UUID)conf.Get("sit_on"),
                Sim = conf.Get("sim"),
                GridName = conf.Contains("grid_name") ? conf.GetString("grid_name") : "agni"
            };
            if (string.IsNullOrEmpty(b.FirstName) || string.IsNullOrEmpty(b.LastName) || string.IsNullOrEmpty(b.Password))
                throw new Exception("Incomplete bot information, first_name, last_name and password are required");
            try
            {
                float pos_x = conf.GetFloat("pos_x");
                float pos_y = conf.GetFloat("pos_y");
                float pos_z = conf.GetFloat("pos_z");
                if (!string.IsNullOrEmpty(b.Sim))
                {
                    b.PosInSim = new Vector3(pos_x, pos_y, pos_z);
                }
            }
            catch { }
            if (conf.Contains("login_uri"))
            {
                b.LoginURI = conf.Get("login_uri");
            }
            return b;
        }
    }

    public class GridBot : IDisposable, IRelay
    {
        public GridClient Client = new GridClient();
        public BotInfo Conf;
        public AInfo GetConf() => Conf;
        public bool LoggingIn = false;

        Configuration MainConf;
        Program MainProgram;
        bool persist = false;
        System.Timers.Timer networkChecker;
        System.Timers.Timer positionChecker;

        public bool Persistent
        {
            set
            {
                persist = value;
                if (value)
                {
                    networkChecker.Enabled = true;
                    positionChecker.Enabled = true;
                }
                else
                {
                    networkChecker.Enabled = false;
                    positionChecker.Enabled = true;
                }
            }
            get => persist;
        }

        public bool IsConnected() => Client != null && Client.Network.Connected;

        public Vector3 Position
            => !IsConnected() ? Vector3.Zero :
            (SittingOn != null && Client.Self.SittingOn == SittingOn.LocalID) ? SittingOn.Position
            : Client.Self.SimPosition;

        private Primitive SittingOn;

        public GridBot(Program p, BotInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
        }

        public void Connect()
        {
            Console.WriteLine($"Logging in {Conf.Name}...");
            if (networkChecker == null)
            {
                networkChecker = new System.Timers.Timer(3 * 60 * 1000)
                {
                    Enabled = false
                };
                networkChecker.Elapsed += new System.Timers.ElapsedEventHandler(NetworkChecker_Elapsed);
            }

            if (positionChecker == null)
            {
                positionChecker = new System.Timers.Timer(60 * 1000)
                {
                    Enabled = false
                };
                positionChecker.Elapsed += new System.Timers.ElapsedEventHandler(PositionChecker_Elapsed);
            }

            Login();
            Persistent = true;
        }

        public void SetBotInfo(BotInfo c) => Conf = c;

        void StatusMsg(string msg)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendFormat("{0:s}]: ", DateTime.Now);
            if (IsConnected())
            {
                sb.Append($" [{Client.Self.FirstName} {Client.Self.LastName}]");
            }
            sb.Append(msg);
            Console.WriteLine($"{sb}");
        }

        void PositionChecker_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!IsConnected()) return;

            if (Conf.SimHandle != 0 && Client.Network.CurrentSim.Handle != Conf.SimHandle)
            {
                StatusMsg("Teleporting to " + Conf.Sim);
                Client.Self.RequestTeleport(Conf.SimHandle, Conf.PosInSim == Vector3.Zero ? new Vector3(128, 128, 30) : Conf.PosInSim);
            }
            else if (Conf.SimHandle == Client.Network.CurrentSim.Handle)
            {
                if (Conf.SitOn != UUID.Zero && Client.Self.SittingOn == 0)
                {
                    Client.Self.RequestSit(Conf.SitOn, Vector3.Zero);
                    Client.Self.Sit();
                }
            }
        }

        void NetworkChecker_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!IsConnected())
            {
                StatusMsg(Conf.Name + " not logged in, trying to log in.");
                if (!LoggingIn)
                {
                    Login();
                }
            }
        }

        private void InitializeClient()
        {
            //reinitialize SecondLife object
            CleanUp();

            Client = new GridClient();

            Client.Settings.USE_INTERPOLATION_TIMER = false;

            // Optimize the throttle
            Client.Throttle.Total = 15000000f;
            Client.Throttle.Texture = 15000000f;
            Client.Throttle.Wind = 0;
            Client.Throttle.Cloud = 0;
            Client.Throttle.Land = 0;
            Client.Settings.ALWAYS_DECODE_OBJECTS = false;
            Client.Settings.ALWAYS_REQUEST_OBJECTS = false;
            Client.Settings.OBJECT_TRACKING = false;
            Client.Settings.AVATAR_TRACKING = false;
            Client.Settings.PARCEL_TRACKING = false;
            Client.Settings.SIMULATOR_TIMEOUT = 120 * 1000;
            Client.Settings.LOGIN_TIMEOUT = 90 * 1000;
            Client.Settings.STORE_LAND_PATCHES = false;
            Client.Settings.MULTIPLE_SIMS = false;
            Client.Self.Movement.Camera.Far = 5.0f;
    
            Client.Settings.USE_ASSET_CACHE = true;
            Client.Settings.ASSET_CACHE_DIR = "./cache";
            Client.Assets.Cache.AutoPruneEnabled = false;

            // Event handlers
            Client.Network.SimChanged += new EventHandler<SimChangedEventArgs>(Network_SimChanged);
            Client.Network.Disconnected += new EventHandler<DisconnectedEventArgs>(Network_Disconnected);
            Client.Network.LoginProgress += new EventHandler<LoginProgressEventArgs>(Network_LoginProgress);
            Client.Self.IM += new EventHandler<InstantMessageEventArgs>(Self_IM);
            Client.Self.ChatFromSimulator += new EventHandler<ChatEventArgs>(LocalChat);
            Client.Objects.ObjectUpdate += new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
        }

        public void CleanUp()
        {
            if (Client != null)
            {
                Client.Network.SimChanged -= new EventHandler<SimChangedEventArgs>(Network_SimChanged);
                Client.Network.Disconnected -= new EventHandler<DisconnectedEventArgs>(Network_Disconnected);
                Client.Network.LoginProgress -= new EventHandler<LoginProgressEventArgs>(Network_LoginProgress);
                Client.Self.IM -= new EventHandler<InstantMessageEventArgs>(Self_IM);
                Client.Self.ChatFromSimulator -= new EventHandler<ChatEventArgs>(LocalChat);
                Client.Objects.ObjectUpdate -= new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);

                SittingOn = null;
                Client = null;
            }
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
                Logout();

                if (networkChecker != null)
                {
                    networkChecker.Dispose();
                    networkChecker = null;
                }

                if (positionChecker != null)
                {
                    positionChecker.Dispose();
                    positionChecker = null;
                }

                CleanUp();
            }
        }

        void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if (e.Prim.LocalID == Client.Self.SittingOn)
            {
                SittingOn = e.Prim;
            }
        }

        void Network_LoginProgress(object sender, LoginProgressEventArgs e)
        {
            if (e.Status == LoginStatus.Success)
            {
                StatusMsg("Logged in");
            }
            else if (e.Status == LoginStatus.Failed)
            {
                StatusMsg($"Failed to login ({e.FailReason}): {e.Message}");
            }
        }

        void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            StatusMsg("Disconnected");
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Thread.Sleep(30 * 1000);
                if (Persistent && !LoggingIn)
                {
                    Login();
                }
            });
        }

        string Strip(string name) => name.EndsWith(" Resident") ? name.Substring(0, name.Length - 9) : name;

        void Self_IM(object sender, InstantMessageEventArgs e)
        {
            if ( e.IM.FromAgentName == Client.Self.Name) return;
            string name = Strip(e.IM.FromAgentName);
            StatusMsg($"{e.IM.Dialog}({name}): {e.IM.Message}");
            if (e.IM.FromAgentID != UUID.Zero && e.IM.FromAgentID != Client.Self.AgentID)
                MainProgram.RelayMessage(Program.EBridgeType.GRID,
                    b => b.Bot == Conf && b?.GridGroup == e.IM.IMSessionID,
                    $"(grid:{Conf.GridName}) {name}", e.IM.Message);

            if (!MainConf.IsMaster(e.IM.FromAgentID))
            {
                if (!e.IM.GroupIM && e.IM.IMSessionID != e.IM.ToAgentID &&
                    (e.IM.Dialog.Equals(InstantMessageDialog.MessageFromAgent) || e.IM.Dialog.Equals(InstantMessageDialog.InventoryOffered)))
                {
                    ReplyIm(e.IM, "Sorry, I am a chat relay bot, you were probably meaning to talk to my masters in some group."
                        + "\nIf you are sending me a texture, might I suggest uploading it to somewhere such as imgur and posting the link in chat? I'll gladly let my masters know then!");
                    Console.WriteLine($"I just sent a response message to {e.IM.FromAgentName}:\n{e.IM}");
                }
                return;
            }

            ThreadPool.QueueUserWorkItem(sync =>
            {
                switch (e.IM.Dialog)
                {
                    case InstantMessageDialog.RequestTeleport:
                        StatusMsg($"Master {e.IM.FromAgentName} is sending teleport");
                        Client.Self.TeleportLureRespond(e.IM.FromAgentID, e.IM.IMSessionID, true);
                        break;
                    case InstantMessageDialog.RequestLure:
                        StatusMsg($"Master {e.IM.FromAgentName} is requesting teleport");
                        Client.Self.SendTeleportLure(e.IM.FromAgentID);
                        break;
                    case InstantMessageDialog.FriendshipOffered:
                        Client.Friends.AcceptFriendship(e.IM.FromAgentID, e.IM.IMSessionID);
                        break;
                    case InstantMessageDialog.MessageFromAgent:
                        ProcessMessage(e.IM);
                        break;
                    case InstantMessageDialog.GroupInvitation:
                        Client.Self.InstantMessage(Client.Self.Name, e.IM.FromAgentID, string.Empty, e.IM.IMSessionID, InstantMessageDialog.GroupInvitationAccept, InstantMessageOnline.Online, Vector3.Zero, UUID.Zero, null);
                        break;
                }
            });
        }

        private void LocalChat(object sender, ChatEventArgs e)
        {
            if (e.FromName == Client.Self.Name) return;
            string name = Strip(e.FromName);
            string begin = string.Empty;
            switch (e.Type)
            {
                case ChatType.StartTyping:
                case ChatType.StopTyping:
                    return;
                case ChatType.RegionSayTo:
                case ChatType.RegionSay:
                    begin = "[RegionWide]";
                    break;
                case ChatType.Whisper:
                    begin = "/me whispers";
                    break;
                case ChatType.Shout:
                    begin = "/me shouts";
                    break;
                default:
                    begin = ":";
                    break;
            }
            StatusMsg($"{e.Type}({name}){begin} {e.Message}");
            if (e.SourceID != UUID.Zero && e.SourceID != Client.Self.AgentID)
                MainProgram.RelayMessage(Program.EBridgeType.GRID,
                    b => b.Bot == Conf && b.GridGroup == null,
                    $"(grid:{Conf.GridName}) {name}", $"{begin} {e.Message}");
        }

        object SyncJoinSession = new object();
        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            if (!Client.Network.Connected) return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                Action formatmsg = () => msg = $"{from}{(msg.StartsWith("/me ") ? msg.Substring(3) : $": {msg}")}";
                if (bridge.GridGroup == null) // null is local
                {
                    var type = ChatType.Normal;
                    string cmd;
                    if (msg.StartsWith(cmd = "!shout "))
                        type = ChatType.Shout;
                    else if (msg.StartsWith(cmd = "!whisper "))
                        type = ChatType.Whisper;
                    if (type != ChatType.Normal) msg = msg.Remove(cmd.Length);
                    formatmsg();
                    Client.Self.Chat(msg, 0, type);
                    return;
                }
                formatmsg();

                UUID groupID = (UUID)bridge.GridGroup;
                bool success = true;
                lock (SyncJoinSession)
                {
                    if (!Client.Self.GroupChatSessions.ContainsKey(groupID))
                    {
                        var joined = new ManualResetEvent(false);
                        EventHandler<GroupChatJoinedEventArgs> handler = (sender, e) =>
                        {
                            if (e.SessionID == groupID)
                            {
                                joined.Set();
                                Logger.Log($"{(e.Success ? "Successfully joined" : "Failed to join")} group chat {groupID}", Helpers.LogLevel.Info);
                            }
                        };
                        success = false;
                        Client.Self.GroupChatJoined += handler;
                        Client.Self.RequestJoinGroupChat(groupID);
                        success = joined.WaitOne(30 * 1000);
                        Client.Self.GroupChatJoined -= handler;
                    }
                }
                if (success)
                {
                    Client.Self.InstantMessageGroup(groupID, msg);
                }
                else
                {
                    Logger.Log("Failed to start group chat session", Helpers.LogLevel.Warning);
                }
            });
        }

        void ReplyIm(InstantMessage im, string msg)
            => Client.Self.InstantMessage(im.FromAgentID, msg, im.IMSessionID);

        void ProcessMessage(InstantMessage im)
        {
            var args = im.Message.Trim().Split(' ');
            if (args.Length < 1) return;
            switch (args[0])
            {
                case "logout":
                    Persistent = false;
                    ReplyIm(im, "OK. Bye.");
                    Client.Network.Logout();
                    break;
                case "startup":
                    ReplyIm(im, "Starting offline bots.");
                    MainProgram.CmdStartup();
                    break;
                case "shutdown":
                    ReplyIm(im, "Logging off all bots.");
                    MainProgram.CmdShutdown();
                    break;
                case "status":
                    ReplyIm(im, MainProgram.CmdStatus());
                    break;
                case "appearance":
                    ReplyIm(im, "Setting appearance... if it does not work try rebake");
                    Client.Appearance.RequestSetAppearance();
                    break;
                case "rebake":
                    ReplyIm(im, "Rebaking texture, please wait, this can take a while");
                    Client.Appearance.RequestSetAppearance(true);
                    break;
                case "groupinfo":
                    GetGroupInfo(im);
                    break;
                case "groupactivate":
                    UUID groupID = UUID.Zero;
                    try { UUID.TryParse(args[1].Trim(), out groupID); }
                    catch { }
                    Client.Groups.ActivateGroup(groupID);
                    ReplyIm(im, "Activated group with uuid: " + groupID.ToString());
                    break;
                case "sendteleport":
                    if (UUID.TryParse(args[1].Trim(), out UUID avatarID) && avatarID != UUID.Zero)
                        Client.Self.SendTeleportLure(avatarID);
                    else
                        Console.WriteLine($"Failed to parse uuid or null in command sendteleport: {args[1].Trim()}");
                    break;
                case "siton":
                    if (UUID.TryParse(args[1].Trim(), out UUID objectID) && objectID != UUID.Zero)
                    {
                        if (SittingOn != null)
                        {
                            Client.Self.Stand();
                        }
                        Client.Self.RequestSit(objectID, Vector3.Zero);
                        Client.Self.Sit();
                    }
                    else
                        Console.WriteLine($"Failed to parse uuid or null in command siton: {args[1].Trim()}");
                    break;
                case "help":
                    ReplyIm(im, "Commands:"
                    + "\nhelp - display this message"
                    + "\nlogout - logs me out"
                    + "\nstartup - starts offline bots"
                    + "\nshutdown - logs all bots off"
                    + "\nstatus - gives the status of all bots"
                    + "\nappearance - rebake me"
                    + "\nrebake - rebake me forcefully"
                    + "\ngroupinfo - get info on all my groups"
                    + "\ngroupactivate <UUID> - sets my active group to UUID, if provided"
                    + "\nsendteleport <UUID> - Send a teleport request to avatar"
                    + "\nsiton <UUID> - Sit on object");
                    break;
            }
        }

        void GetGroupInfo(InstantMessage im)
        {
            ReplyIm(im, "Getting my groups...");
            ManualResetEvent finished = new ManualResetEvent(false);

            EventHandler<CurrentGroupsEventArgs> handler = (sender, e) =>
            {
                foreach (var group in e.Groups)
                    ReplyIm(im, $"{group.Key} - {group.Value.Name}");
                finished.Set();
            };
            Client.Groups.CurrentGroups += handler;
            Client.Groups.RequestCurrentGroups();
            finished.WaitOne(30 * 1000);
            Client.Groups.CurrentGroups -= handler;
        }

        void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            if (Conf.SitOn != UUID.Zero && Conf.Sim.ToLower() == Client.Network.CurrentSim.Name.ToLower())
            {
                Client.Self.RequestSit(Conf.SitOn, Vector3.Zero);
                Client.Self.Sit();
            }
            Client.Network.CurrentSim.Pause();
        }

        private void UpdateSimHandle()
        {
            if (!string.IsNullOrEmpty(Conf.Sim))
            {
                Conf.SimHandle = MainConf.GetRegionHandle(Conf.Sim);
                if (Conf.SimHandle == 0)
                {
                    if (Client.Grid.GetGridRegion(Conf.Sim, GridLayerType.Objects, out var region))
                    {
                        Conf.SimHandle = region.RegionHandle;
                        MainConf.SaveCachedRegionHandle(Conf.Sim, Conf.SimHandle);
                    }
                }
            }
        }

        public bool Login()
        {
            lock (this)
            {
                if (LoggingIn)
                {
                    return false;
                }
                LoggingIn = true;
            }
            StatusMsg("Logging in " + Conf.Name);

            ThreadPool.QueueUserWorkItem(sync =>
            {
                InitializeClient();
                LoginParams param = Client.Network.DefaultLoginParams(Conf.FirstName, Conf.LastName, Conf.Password, "BarkBot", Program.Version);
                param.URI = Conf.LoginURI;
                param.Start = "last";
                if (Client.Network.Login(param))
                {
                    UpdateSimHandle();
                }
                LoggingIn = false;
            });
            return true;
        }

        public void Logout()
        {
            Persistent = false;
            Client.Network.Logout();
        }
    }
}
