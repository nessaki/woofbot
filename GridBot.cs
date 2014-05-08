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
using System.Text;
using System.Threading;
using OpenMetaverse;

namespace Jarilo
{
    public class GridBot : IDisposable, IRelay
    {
        public GridClient Client = new GridClient();
        public BotInfo Conf;
        public bool LoggingIn = false;

        Configuration MainConf;
        Program MainProgram;
        bool persit = false;
        System.Timers.Timer networkChecker;
        System.Timers.Timer positionChecker;

        public bool Persitant
        {
            set
            {
                persit = value;
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
            get
            {
                return persit;
            }
        }

        public bool Connected
        {
            get
            {
                if (Client != null && Client.Network.Connected)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public Vector3 Position
        {
            get
            {
                if (!Connected) return Vector3.Zero;

                if (SittingOn != null && Client.Self.SittingOn == SittingOn.LocalID)
                    return SittingOn.Position;
                else
                    return Client.Self.SimPosition;
            }
        }

        private Primitive SittingOn;

        public GridBot(Program p, BotInfo c, Configuration m)
        {
            MainProgram = p;
            Conf = c;
            MainConf = m;
            networkChecker = new System.Timers.Timer(3 * 60 * 1000);
            networkChecker.Enabled = false;
            networkChecker.Elapsed += new System.Timers.ElapsedEventHandler(networkChecker_Elapsed);
            positionChecker = new System.Timers.Timer(60 * 1000);
            positionChecker.Enabled = false;
            positionChecker.Elapsed += new System.Timers.ElapsedEventHandler(positionChecker_Elapsed);
        }

        public void SetBotInfo(BotInfo c)
        {
            Conf = c;
        }

        void StatusMsg(string msg)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendFormat("{0:s}]: ", DateTime.Now);
            if (Connected)
            {
                sb.AppendFormat(" [{0} {1}]", Client.Self.FirstName, Client.Self.LastName);
            }
            sb.Append(msg);
            System.Console.WriteLine(sb.ToString());
        }

        void positionChecker_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!Connected) return;

            if (Conf.SimHandle != 0 && Client.Network.CurrentSim.Handle != Conf.SimHandle)
            {
                Vector3 tpPos;
                if (Conf.PosInSim == Vector3.Zero)
                {
                    tpPos = new Vector3(128, 128, 30);
                }
                else
                {
                    tpPos = Conf.PosInSim;
                }

                StatusMsg("Teleporting to " + Conf.Sim);
                Client.Self.RequestTeleport(Conf.SimHandle, tpPos);
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

        void networkChecker_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!Connected)
            {
                StatusMsg(Conf.Name + " not loggend in, trying to log in.");
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
            Client.Objects.ObjectUpdate += new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
        }

        public void CleanUp()
        {
            if (Client == null) return;

            Client.Network.SimChanged -= new EventHandler<SimChangedEventArgs>(Network_SimChanged);
            Client.Network.Disconnected -= new EventHandler<DisconnectedEventArgs>(Network_Disconnected);
            Client.Network.LoginProgress -= new EventHandler<LoginProgressEventArgs>(Network_LoginProgress);
            Client.Self.IM -= new EventHandler<InstantMessageEventArgs>(Self_IM);
            Client.Objects.ObjectUpdate -= new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);

            SittingOn = null;
            Client = null;
        }

        public void Dispose()
        {
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
                StatusMsg(string.Format("Failed to login ({0}): {1}", e.FailReason, e.Message));
            }
        }

        void Network_Disconnected(object sender, DisconnectedEventArgs e)
        {
            StatusMsg("Disconnected");
            ThreadPool.QueueUserWorkItem(sync =>
            {
                Thread.Sleep(30 * 1000);
                if (Persitant && !LoggingIn)
                {
                    Login();
                }
            });
        }

        void Self_IM(object sender, InstantMessageEventArgs e)
        {
            StatusMsg(e.IM.Dialog + "(" + e.IM.FromAgentName + "): " + e.IM.Message);
            if (e.IM.FromAgentName == Client.Self.Name) return;

            List<BridgeInfo> bridges = MainConf.Bridges.FindAll((BridgeInfo b) =>
            {
                return
                    b.Bot == Conf &&
                    b.GridGroup == e.IM.IMSessionID;
            });

            foreach (var bridge in bridges)
            {
                string name = e.IM.FromAgentName.EndsWith(" Resident") ? e.IM.FromAgentName.Substring(0, e.IM.FromAgentName.Length - 9) : e.IM.FromAgentName;
                string from = string.Format("(grid:{0}) {1}", Conf.GridName, name);

                IrcBot ircbot = MainProgram.IrcBots.Find((IrcBot ib) => { return ib.Conf == bridge.IrcServerConf; });
                if (ircbot != null && bridge.GridGroup == e.IM.IMSessionID && e.IM.FromAgentID != UUID.Zero && e.IM.FromAgentID != Client.Self.AgentID)
                {
                    ircbot.RelayMessage(bridge, from, e.IM.Message);
                }
                XmppBot xmppbot = MainProgram.XmppBots.Find((XmppBot ib) => { return ib.Conf == bridge.XmppServerConf; });
                if (xmppbot != null && bridge.GridGroup == e.IM.IMSessionID && e.IM.FromAgentID != UUID.Zero && e.IM.FromAgentID != Client.Self.AgentID)
                {
                    xmppbot.RelayMessage(bridge, from, e.IM.Message);
                }
            }

            if (!MainConf.IsMaster(e.IM.FromAgentName))
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(sync =>
            {
                switch (e.IM.Dialog)
                {
                    case InstantMessageDialog.RequestTeleport:
                        StatusMsg("Master is requesting teleport");
                        Client.Self.TeleportLureRespond(e.IM.FromAgentID, true);
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

        object SyncJoinSession = new object();

        public void RelayMessage(BridgeInfo bridge, string from, string msg)
        {
            if (!Client.Network.Connected) return;

            ThreadPool.QueueUserWorkItem(sync =>
                {
                    UUID groupID = bridge.GridGroup;
                    bool success = true;
                    lock (SyncJoinSession)
                    {
                        if (!Client.Self.GroupChatSessions.ContainsKey(groupID))
                        {
                            ManualResetEvent joined = new ManualResetEvent(false);
                            EventHandler<GroupChatJoinedEventArgs> handler = (object sender, GroupChatJoinedEventArgs e) =>
                                {
                                    if (e.SessionID == groupID)
                                    {
                                        joined.Set();
                                        Logger.Log(string.Format("{0} group chat {1}", e.Success ? "Successfully joined" : "Failed to join", groupID), Helpers.LogLevel.Info);
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
                        if (msg.StartsWith("/me "))
                            Client.Self.InstantMessageGroup(groupID, string.Format("{0}{1}", from, msg.Substring(3)));
                        else
                            Client.Self.InstantMessageGroup(groupID, string.Format("{0}: {1}", from, msg));
                    }
                    else
                    {
                        Logger.Log("Failed to start group chat session", Helpers.LogLevel.Warning);
                    }
                }
            );
        }

        void ReplyIm(InstantMessage im, string msg)
        {
            Client.Self.InstantMessage(im.FromAgentID, msg, im.IMSessionID);
        }

        void ProcessMessage(InstantMessage im)
        {
            string[] args = im.Message.Trim().Split(' ');
            if (args.Length < 1) return;
            switch (args[0])
            {
                case "logout":
                    Persitant = false;
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
                    UUID group = UUID.Zero;
                    try { UUID.TryParse(im.Message.Split(' ')[1].Trim(), out group); }
                    catch { }
                    Client.Groups.ActivateGroup(group);
                    ReplyIm(im, "Activated group with uuid: " + group.ToString());
                    break;
            }
        }

        void GetGroupInfo(InstantMessage im)
        {
            ReplyIm(im, "Getting my groups...");
            ManualResetEvent finished = new ManualResetEvent(false);

            EventHandler<CurrentGroupsEventArgs> handler = (object sender, CurrentGroupsEventArgs e) =>
                {
                    foreach (var group in e.Groups)
                    {
                        ReplyIm(im, string.Format("{0} - {1}", group.Key, group.Value.Name));
                    }
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
                    GridRegion region;
                    if (Client.Grid.GetGridRegion(Conf.Sim, GridLayerType.Objects, out region))
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
                LoginParams param = Client.Network.DefaultLoginParams(Conf.FirstName, Conf.LastName, Conf.Password, "Jarilo", Program.Version);
                param.URI = Conf.LoginURI;
                param.Start = "last";
                bool success = Client.Network.Login(param);
                if (success)
                {
                    UpdateSimHandle();
                }
                LoggingIn = false;
            });
            return true;
        }

        public void Logout()
        {
            Persitant = false;
            Client.Network.Logout();
        }
    }
}
