using System;
using HarmonyLib;

namespace OaMod
{
    /// <summary>
    /// The game sizes its per-user ball cache when the match starts and only ever
    /// shrinks it: with more users than slots it writes past the end every frame
    /// (IndexOutOfRangeException in CacheUsersBallMovements, the whole game flow stops).
    /// Dropping the stale array makes the game allocate one of the right size.
    /// </summary>
    [HarmonyPatch(typeof(GameFlowController), nameof(GameFlowController.CacheUsersBallMovements))]
    internal static class BallCacheGrowPatch
    {
        private static bool _failed;

        private static void Prefix(GameFlowController __instance)
        {
            if (_failed) return;
            try
            {
                // ponytail: the getter allocates a wrapper every frame; cache the field pointer if it shows up in a profile.
                var balls = __instance.m_CachedUsersBallMovements;
                int users = __instance.m_nCachedUserCount;
                if (balls == null || balls.Length >= users) return;

                __instance.m_CachedUsersBallMovements = null;
                Plugin.Logs.LogInfo($"LateJoin: ball cache had {balls.Length} slot(s) for {users} user(s); dropped it so the game reallocates.");
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Logs.LogWarning($"LateJoin: ball cache fix unavailable ({e.GetType().Name}: {e.Message}); off for this session.");
            }
        }
    }
}

namespace OaMod
{
    /// <summary>
    /// Photon replays every instantiate to a late joiner the moment they enter the
    /// room, while they are still in the lobby scene, so the course load destroys
    /// those objects and the joiner never sees the other balls. Keep each
    /// instantiate event as it was sent so the host can send the balls again
    /// once the joiner is in the course.
    /// </summary>
    [HarmonyPatch(typeof(NetworkingPeer), nameof(NetworkingPeer.DoInstantiate))]
    internal static class InstantiateLog
    {
        // ponytail: entries of destroyed objects stay until we leave the room; a match makes a few dozen at most.
        internal static readonly System.Collections.Generic.Dictionary<int, ExitGames.Client.Photon.Hashtable> ByViewId = new();

        private static void Postfix(ExitGames.Client.Photon.Hashtable evData, UnityEngine.GameObject __result)
        {
            try
            {
                if (evData == null || __result == null) return;
                var view = __result.GetComponent<PhotonView>();
                if (view != null) ByViewId[view.viewID] = evData;
            }
            catch (Exception e) { Plugin.Logs.LogWarning($"LateJoin: could not keep an instantiate event ({e.GetType().Name}: {e.Message})."); }
        }

        /// <summary>Send the other users' balls to one player again. Returns how many went out.</summary>
        internal static int ResendBalls(User joiner)
        {
            var users = Team17.Services.Users.GetUsers(out int count);
            var options = new RaiseEventOptions { TargetActors = new[] { joiner.PhotonPlayer.ID } };
            int sent = 0;
            for (int i = 0; i < count; i++)
            {
                var ball = users[i] == null ? null : users[i].Ball;
                if (ball == null || users[i].Pointer == joiner.Pointer) continue;
                var view = ball.GetComponent<PhotonView>();
                if (view == null || !ByViewId.TryGetValue(view.viewID, out var evData)) continue;
                // 202 is PUN's Instantiation event; not cached, so the room's own copy stays as it is.
                if (PhotonNetwork.networkingPeer.OpRaiseEvent(202, evData, true, options)) sent++;
                Plugin.Logs.LogInfo($"LateJoin: ball view {view.viewID} of '{users[i].DisplayName}' syncs as {view.synchronization}");
            }
            return sent;
        }
    }
}
