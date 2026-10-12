using System;
using Cinemachine;
using UnityEngine;

namespace OaMod
{
    /// <summary>
    /// Both features in one component: the host-side room reopen and the local fly camera.
    /// A failure in either switches that feature off for the session and leaves the other running.
    /// </summary>
    public class OaBehaviour : MonoBehaviour
    {
        public OaBehaviour(IntPtr ptr) : base(ptr) { }

        private bool _roomFailed;
        private float _nextRoomCheck;
        private float _nextJoinerCheck;
        private bool _refreshing;

        private bool _avatarFailed;
        private float _nextAvatarCheck;
        private readonly System.Collections.Generic.Dictionary<ulong, int> _avatarTries = new();

        private bool _flyFailed;
        private bool _flying;
        private Transform _cam;
        private CinemachineBrain _brain;
        private float _yaw;
        private float _pitch;
        private Vector3 _lastMouse;
        private bool _mouseAxisFailed;

        private void Update()
        {
            if (!_roomFailed)
            {
                try { KeepRoomOpen(); DriveLateJoiners(); }
                catch (Exception e)
                {
                    _roomFailed = true;
                    Plugin.Logs.LogWarning($"Room: unavailable ({e.GetType().Name}: {e.Message}); keep-open and late join are off for this session.");
                }
            }

            if (!_avatarFailed)
            {
                try { FixAvatars(); }
                catch (Exception e)
                {
                    _avatarFailed = true;
                    Plugin.Logs.LogWarning($"Avatar: unavailable ({e.GetType().Name}: {e.Message}); late joiners keep the plain icon this session.");
                }
            }

            if (!_flyFailed)
            {
                try { FlyCam(); }
                catch (Exception e)
                {
                    _flyFailed = true;
                    Plugin.Logs.LogWarning($"FlyCam: unavailable ({e.GetType().Name}: {e.Message}); off for this session.");
                    try { StopFlying(); } catch { }
                }
            }
        }

        // The game closes the Photon room when the host leaves the lobby
        // (FinalizeLobbyGameState.OnStart) and its Steam coordinator mirrors
        // Room.IsOpen onto the Steam lobby, so reopening the room is the whole job.
        private void KeepRoomOpen()
        {
            if (!Plugin.KeepRoomOpen.Value || Time.unscaledTime < _nextRoomCheck) return;
            _nextRoomCheck = Time.unscaledTime + 1f;

            if (!PhotonNetwork.isMasterClient) return;
            var room = PhotonNetwork.room;
            if (room == null || room.IsOpen) return;

            room.IsOpen = true;
            Plugin.Logs.LogInfo($"Room: reopened '{room.Name}' ({room.PlayerCount}/{room.MaxPlayers}); friends can join by invite.");
        }

        // The host normally walks every client from the lobby into the match by
        // setting their network state, while it passes through the same states
        // itself. Once the host is in gameplay nobody does that for a newcomer,
        // so they sit in the lobby. Do the host's half of each step for them.
        private void DriveLateJoiners()
        {
            if (!Plugin.KeepRoomOpen.Value || Time.unscaledTime < _nextJoinerCheck) return;
            _nextJoinerCheck = Time.unscaledTime + 1f;

            if (PhotonNetwork.room == null) InstantiateLog.ByViewId.Clear();
            if (!Team17.Services.Exists()) return;
            bool master = PhotonNetwork.isMasterClient;
            var service = Team17.Services.Users;
            if (service == null) return;
            var me = service.GetPrimaryLocalUser();
            if (me == null || me.NetworkState != NetworkUserState.InGameplay) return;

            bool arriving = false;
            var users = service.GetUsers(out int count);
            for (int i = 0; i < count; i++)
            {
                var user = users[i];
                if (user == null || user.IsLocal) continue;

                var state = user.NetworkState;
                if (state == NetworkUserState.InGameplay)
                {
                    var flow = user.GameFlowState;
                    arriving |= flow >= GameFlowUserState.SpawnPlayers && flow <= GameFlowUserState.GameplayStarted;
                    if (master) DriveGameFlow(user, me.GameFlowState);
                    continue;
                }
                if (!master) continue;

                NetworkUserState next;
                switch (state)
                {
                    case NetworkUserState.InLobby: next = NetworkUserState.ClosingLobby; break;
                    case NetworkUserState.DownloadComplete: next = NetworkUserState.Loading; break;
                    case NetworkUserState.WaitingToPlay: next = NetworkUserState.InGameplay; break;
                    default: continue;
                }
                user.NetworkState = next;
                Plugin.Logs.LogInfo($"LateJoin: '{user.DisplayName}' {state} -> {next}");
            }

            // Photon sends only the values that changed, and a receiver without a full first
            // update drops those. The newcomer's copy of our ball is brand new, so start over
            // with full updates while they spawn. Every client in the match does this for its own ball.
            if (arriving && me.GameFlowState >= GameFlowUserState.HoleStarting)
            {
                PhotonNetwork.networkingPeer.ResetPhotonViewsOnSerialize();
                if (!_refreshing) Plugin.Logs.LogInfo("LateJoin: sending full ball updates while a newcomer spawns.");
            }
            _refreshing = arriving;
        }

        // Inside the match the same handshake repeats on the game flow state: each
        // client reports a setup step done and the host answers once ALL users report
        // it. A host that is already playing a hole never matches, so the newcomer
        // sits on "Waiting For Other Players". Answer for that one user instead.
        private static void DriveGameFlow(User user, GameFlowUserState mine)
        {
            // Only while the host is inside a hole cycle; never during game end or the special modes.
            if (mine < GameFlowUserState.HoleStarting || mine > GameFlowUserState.NextHoleStarted) return;

            var state = user.GameFlowState;
            GameFlowUserState next;
            switch (state)
            {
                case GameFlowUserState.LevelSetupFinished: next = GameFlowUserState.SpawnPlayers; break;
                case GameFlowUserState.SpawnedPlayers: next = GameFlowUserState.GameplayStarting; break;
                case GameFlowUserState.GameplayStarted:
                    // While the host plays a hole the newcomer starts one too. Around the scoreboard the
                    // host waits for ALL users to sit in its own state, so a newcomer sent off to play
                    // would hang the scoreboard for everyone: park them next to the host instead, and
                    // they leave for the next hole with the rest.
                    if (mine < GameFlowUserState.AllInHoleStarting) next = GameFlowUserState.HoleStarting;
                    else if (mine == GameFlowUserState.AllInHoleStarted || mine == GameFlowUserState.IntermissionStarted
                             || mine == GameFlowUserState.NextHoleStarted) next = mine;
                    else return; // the host is between two waits; next tick
                    break;
                default: return;
            }
            user.GameFlowState = next;
            Plugin.Logs.LogInfo($"LateJoin: '{user.DisplayName}' {state} -> {next} (host at {mine})");
            // Their course is loaded from here on, so the balls can finally exist on their side.
            if (state == GameFlowUserState.LevelSetupFinished)
                Plugin.Logs.LogInfo($"LateJoin: sent {InstantiateLog.ResendBalls(user)} ball(s) to '{user.DisplayName}' again");
        }

        // The game asks Steam for other players' avatars only from its lobby screens.
        // Someone who joins while we are already in the match never gets asked for, and
        // the scoreboard shows a plain ball. Ask Steam ourselves; only our own screen changes.
        private void FixAvatars()
        {
            if (Time.unscaledTime < _nextAvatarCheck) return;
            _nextAvatarCheck = Time.unscaledTime + 2f;

            if (!Team17.Services.Exists()) return;
            var service = Team17.Services.Users;
            var friends = Team17.Services.Friends;
            if (service == null || friends == null) return;
            var me = service.GetPrimaryLocalUser();
            if (me == null || me.NetworkState != NetworkUserState.InGameplay) return;

            var users = service.GetUsers(out int count);
            for (int i = 0; i < count; i++)
            {
                var user = users[i];
                if (user == null || user.IsLocal || user.NetworkState != NetworkUserState.InGameplay) continue;
                if (user.ProfilePicture != null || user.PlatformID == null) continue;

                var id = user.PlatformID.SteamId;
                _avatarTries.TryGetValue(id.m_SteamID, out int tries);
                if (tries > 30) continue; // a private profile may never answer
                _avatarTries[id.m_SteamID] = tries + 1;

                // 0: no avatar, -1: Steam is still loading it, otherwise an image handle.
                int handle = Steamworks.SteamFriends.GetLargeFriendAvatar(id);
                if (handle <= 0)
                {
                    if (tries == 0)
                    {
                        Steamworks.SteamFriends.RequestUserInformation(id, false);
                        Plugin.Logs.LogInfo($"Avatar: '{user.DisplayName}' has no picture (Steam handle {handle}); asked Steam for it.");
                    }
                    continue;
                }

                // The game's own conversion, so the picture comes out the same way up as everyone else's.
                var picture = friends.m_avatarCoordinator.MakeTexture(handle);
                if (picture == null) continue;
                user.ProfilePicture = picture;

                int rows = 0;
                foreach (var row in FindObjectsOfType<ScoreboardPlayerBinderBase>())
                {
                    var data = row.GetData();
                    if (data == null || data.m_User == null || data.m_User.Pointer != user.Pointer) continue;
                    row.SetData(data); // re-reads the picture
                    rows++;
                }
                Plugin.Logs.LogInfo($"Avatar: '{user.DisplayName}' set {picture.width}x{picture.height} from Steam; {rows} scoreboard row(s) refreshed.");
            }
        }

        private void FlyCam()
        {
            var key = Plugin.FlyKey.Value;
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                if (_flying) StopFlying();
                else StartFlying();
            }
            if (!_flying) return;

            // A scene change destroys the camera under us.
            if (_cam == null) { StopFlying(); return; }

            Vector2 look = MouseDelta() * Plugin.LookSensitivity.Value;
            _yaw += look.x;
            _pitch = Mathf.Clamp(_pitch - look.y, -89f, 89f);
            _cam.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            float speed = Plugin.FlySpeed.Value * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.LeftShift)) speed *= Plugin.FlyFastMultiplier.Value;
            Vector3 move = _cam.forward * Axis(KeyCode.W, KeyCode.S)
                         + _cam.right * Axis(KeyCode.D, KeyCode.A)
                         + Vector3.up * Axis(KeyCode.E, KeyCode.Q);
            _cam.position += move * speed;
        }

        private void StartFlying()
        {
            var main = Camera.main;
            if (main == null)
            {
                Plugin.Logs.LogInfo("FlyCam: no main camera in this scene.");
                return;
            }

            _cam = main.transform;
            // The brain writes the camera transform every frame; with it off the camera is ours.
            _brain = main.GetComponent<CinemachineBrain>();
            if (_brain != null) _brain.enabled = false;

            Vector3 euler = _cam.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _lastMouse = Input.mousePosition;
            _flying = true;
            Plugin.Logs.LogInfo($"FlyCam: on ('{main.name}', {(_brain != null ? "Cinemachine brain paused" : "no Cinemachine brain")}). WASD, Q/E, Left Shift.");
        }

        private void StopFlying()
        {
            if (!_flying) return;
            _flying = false;
            if (_brain != null) _brain.enabled = true;
            _brain = null;
            _cam = null;
            Plugin.Logs.LogInfo("FlyCam: off.");
        }

        private static float Axis(KeyCode positive, KeyCode negative) =>
            (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);

        // The "Mouse X"/"Mouse Y" axes exist only if the game kept Unity's default
        // input axes; the cursor position delta is the fallback (it reads nothing while the cursor is locked).
        private Vector2 MouseDelta()
        {
            if (!_mouseAxisFailed)
            {
                try { return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")); }
                catch (Exception e)
                {
                    _mouseAxisFailed = true;
                    Plugin.Logs.LogWarning($"FlyCam: mouse axes unavailable ({e.GetType().Name}); using the cursor position.");
                }
            }

            Vector3 now = Input.mousePosition;
            Vector3 delta = now - _lastMouse;
            _lastMouse = now;
            return new Vector2(delta.x, delta.y) * 0.1f;
        }
    }
}
