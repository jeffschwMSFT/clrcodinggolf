using ClrCodingGolf.Analysis;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClrCodingGolf.Hubs
{
    public class SubmitHub : Hub
    {
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // get the user
            if (ConnectionDetails.TryGetGroupByUser(Context.ConnectionId, out GroupDetails groupdetails, out UserDetails userdetails))
            {
                // check if owner
                var isowner = groupdetails.IsOwner(userdetails);

                // remove this user
                groupdetails.RemoveUser(userdetails);

                // if the owner was removed, then elevate another user as owner and notify them
                if (isowner)
                {
                    var newowner = groupdetails.GetUsers().FirstOrDefault();
                    if (newowner != null && groupdetails.TryElevateUserToOwner(newowner))
                    {
                        await Clients.Client(newowner.ConnectionId).SendAsync("ReceiveIsOwner");
                    }
                }

                // broadcast current players to everyone in the group
                await SendParticipants(groupdetails.Name);
            }
        }

        public async Task SendJoinGame(string group, string user)
        {
            // get the UserDetails
            var groupdetails = ConnectionDetails.GetOrCreateGroup(group);
            var userdetails = groupdetails.GetOrCreateUser(user, Context.ConnectionId);

            // associate connection with group
            await Groups.AddToGroupAsync(Context.ConnectionId, group);

            // check if owner
            if (groupdetails.IsOwner(userdetails))
            {
                await Clients.Caller.SendAsync("ReceiveIsOwner");
            }

            // broadcast current players to everyone in the group
            await SendParticipants(groupdetails.Name);
        }

        public async Task SendParticipants(string group)
        {            
            // get the group
            if (ConnectionDetails.TryGetGroup(group, out GroupDetails groupdetails))
            {
                var users = groupdetails.GetUsers();
                if (users != null && users.Count > 0)
                {
                    var allusers = users.Select(u => $"{u.ConnectionId},{u.UserName},{u.Rating}").ToList();
                    var json = System.Text.Json.JsonSerializer.Serialize(allusers);
                    await Clients.Group(groupdetails.Name).SendAsync("ReceiveParticipants", json);
                }
            }
        }

        public async Task SendCode(string group, string user, string code)
        {
            // get the group
            if (ConnectionDetails.TryGetGroup(group, out GroupDetails groupdetails))
            {
                // get the user
                if (groupdetails.TryGetUser(Context.ConnectionId, out UserDetails userdetails))
                {
                    // run code analysis
                    CodeMetrics metrics = default(CodeMetrics);
                    float rating = Single.MaxValue;

                    try
                    {
                        metrics = ClrCodingGolf.Analysis.CodeAnalysis.Analyze(code, injectIntrigue: true);
                        System.Diagnostics.Debug.WriteLine($"{metrics.Bytes} {metrics.Lines} {metrics.CCM} {metrics.Characters}");
                        rating = (metrics.Bytes * 0.35f) + (metrics.Lines * 0.05f) + (metrics.CCM * 0.5f) + (metrics.Characters * 0.1f);
                        if (rating < 0) rating = 0;

                        // retain the code and rating
                        userdetails.Attempts++;
                        userdetails.Code = code;
                        userdetails.Rating = rating;
                    }
                    catch (Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine(e);
                    }

                    // share result back
                    await Clients.Caller.SendAsync("ReceiveMyResult", metrics.Bytes, metrics.Lines, metrics.CCM, metrics.Characters, rating);

                    // share with all clients
                    await Clients.All.SendAsync("ReceiveCode", Context.ConnectionId, user, code, rating);
                }
                else
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", "unable to find user (please reload)");
                }
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "unable to find group (please reload)");
            }
        }

        public async Task SendUpdatedCode(string group, string connectionid)
        {
            // send the code for this user to the caller

            // get the group
            if (ConnectionDetails.TryGetGroup(group, out GroupDetails groupdetails))
            {
                // get the user
                if (groupdetails.TryGetUser(connectionid, out UserDetails userdetails))
                {
                    // share with all clients
                    await Clients.Caller.SendAsync("ReceiveCode", connectionid, userdetails.UserName, userdetails.Code, userdetails.Rating);
                }
                else
                {
                    await Clients.Caller.SendAsync("ReceiveMessage", "unable to find user (please reload)");
                }
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveMessage", "unable to find group (please reload)");
            }
        }

        public async Task SendClear(string group)
        {
            // reset all the current code and ratings

            // get the group
            if (ConnectionDetails.TryGetGroup(group, out GroupDetails groupdetails))
            {
                // get the user
                if (groupdetails.TryGetUser(Context.ConnectionId, out UserDetails userdetails))
                {
                    if (groupdetails.IsOwner(userdetails))
                    {
                        // clear all the code/rating
                        groupdetails.ClearCodeAndRatings();

                        // send the clear signal
                        await Clients.Group(group).SendAsync("ReceiveClear");
                    }
                }
            }
        }

        #region private
        class UserDetails
        {
            public string ConnectionId { get; set; }
            public string UserName { get; set; }
            public string Code { get; set; }
            public float Rating { get; set; }
            public int Attempts { get; set; }
        }
        class GroupDetails
        {
            public GroupDetails(string groupname)
            {
                Name = groupname;
                Users = new Dictionary<string, UserDetails>();
                GroupLock = new ReaderWriterLockSlim();
            }

            public string Name { get; private set; }

            public UserDetails GetOrCreateUser(string username, string connectionid)
            {
                try
                {
                    GroupLock.EnterUpgradeableReadLock();
                    UserDetails userdetails = null;
                    if (!Users.TryGetValue(connectionid, out userdetails))
                    {
                        try
                        {
                            GroupLock.EnterWriteLock();
                            if (!Users.TryGetValue(connectionid, out userdetails))
                            {
                                userdetails = new UserDetails() { ConnectionId = connectionid, Code = "", UserName = username, Rating = Single.MaxValue };
                                TryElevateUserToOwner(userdetails);
                                Users.Add(connectionid, userdetails);
                            }
                        }
                        finally
                        {
                            GroupLock.ExitWriteLock();
                        }
                    }

                    return userdetails;
                }
                finally
                {
                    GroupLock.ExitUpgradeableReadLock();
                }
            }

            public bool TryGetUser(string connectionid, out UserDetails userdetails)
            {
                try
                {
                    GroupLock.EnterReadLock();
                    if (Users.TryGetValue(connectionid, out userdetails)) return true;
                    return false;
                }
                finally
                {
                    GroupLock.ExitReadLock();
                }
            }

            public bool RemoveUser(UserDetails user)
            {
                try
                {
                    GroupLock.EnterWriteLock();
                    if (IsOwner(user)) Owner = null;
                    return Users.Remove(user.ConnectionId);
                }
                finally
                {
                    GroupLock.ExitWriteLock();
                }
            }

            public bool IsOwner(UserDetails user)
            {
                return user != null && Owner != null && string.Equals(user.ConnectionId, Owner.ConnectionId, StringComparison.OrdinalIgnoreCase);
            }

            public bool TryElevateUserToOwner(UserDetails userdetails)
            {
                if (Owner != null || userdetails == null) return false;
                Owner = userdetails;
                return true;
            }

            public List<UserDetails> GetUsers()
            {
                try
                {
                    GroupLock.EnterReadLock();
                    return Users.Values.ToList();
                }
                finally
                {
                    GroupLock.ExitReadLock();
                }
            }

            public void ClearCodeAndRatings()
            {
                try
                {
                    GroupLock.EnterReadLock();
                    foreach(var userdetail in Users.Values)
                    {
                        userdetail.Code = "";
                        userdetail.Rating = Single.MaxValue;
                        userdetail.Attempts = 0;
                    }
                }
                finally
                {
                    GroupLock.ExitReadLock();
                }
            }

            #region private
            private Dictionary<string /*connectionid*/, UserDetails> Users;
            private ReaderWriterLockSlim GroupLock;
            private UserDetails Owner;
            #endregion
        }
        static class ConnectionDetails
        {
            public static GroupDetails GetOrCreateGroup(string group)
            {
                try
                {
                    ConnectionsLock.EnterUpgradeableReadLock();
                    GroupDetails groupdetails = null;
                    if (!Connections.TryGetValue(group, out groupdetails))
                    {
                        try
                        {
                            ConnectionsLock.EnterWriteLock();
                            if (!Connections.TryGetValue(group, out groupdetails))
                            {
                                groupdetails = new GroupDetails(groupname: group);
                                Connections.Add(group, groupdetails);
                            }
                        }
                        finally
                        {
                            ConnectionsLock.ExitWriteLock();
                        }
                    }
                    return groupdetails;
                }
                finally
                {
                    ConnectionsLock.ExitUpgradeableReadLock();
                }
            }

            public static bool TryGetGroup(string group, out GroupDetails groupdetails)
            {
                try
                {
                    ConnectionsLock.EnterReadLock();
                    if (Connections.TryGetValue(group, out groupdetails)) return true;
                    return false;
                }
                finally
                {
                    ConnectionsLock.ExitReadLock();
                }
            }

            public static bool TryGetGroupByUser(string connectionid, out GroupDetails groupdetails, out UserDetails userdetails)
            {
                try
                {
                    ConnectionsLock.EnterReadLock();
                    foreach(var connection in Connections.Values)
                    {
                        if (connection.TryGetUser(connectionid, out userdetails))
                        {
                            groupdetails = connection;
                            return true;
                        }
                    }
                    groupdetails = null;
                    userdetails = null;
                    return false;
                }
                finally
                {
                    ConnectionsLock.ExitReadLock();
                }
            }

            #region private
            private static ReaderWriterLockSlim ConnectionsLock = new ReaderWriterLockSlim();
            private static Dictionary<string /*group*/, GroupDetails> Connections = new Dictionary<string, GroupDetails>();
            #endregion
        }
        #endregion
    }
}
