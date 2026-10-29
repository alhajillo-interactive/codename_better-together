// C1.8 smoke test: two NetworkRunners in one process (Fusion multi-peer) join a Shared session, one peer grabs and
// releases a carryable through the same public API the input layer uses, and both peers must agree it is held and
// then resting on the ground. The game code lives in Assembly-CSharp, which an assembly definition cannot reference,
// so every game type is reached by reflection (see Game below). Fusion types are used directly.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BetterTogether.Tests
{
    public class SmokeMultiPeerTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/CampBryan.unity";
        private const float StepTimeout = 30f;
        private const float GrabTimeout = 2f;
        private const float DropSettleSeconds = 3f;
        private const float MaxHeightAboveGround = 0.05f;
        private const float MaxPeerDifference = 0.10f;

        // Network problems are not game regressions: the test is skipped for these.
        private static readonly HashSet<ShutdownReason> NetworkReasons = new HashSet<ShutdownReason>
        {
            ShutdownReason.PhotonCloudTimeout, ShutdownReason.ConnectionTimeout, ShutdownReason.ConnectionRefused,
            ShutdownReason.OperationTimeout, ShutdownReason.MaxCcuReached, ShutdownReason.InvalidRegion,
        };

        [UnityTest, Timeout(120000)]
        public IEnumerator TwoPeers_GrabAndRelease_AgreeAndRestOnGround()
        {
            var config = NetworkProjectConfig.Global;
            var previousPeerMode = config.PeerMode;
            var previousIgnore = LogAssert.ignoreFailingMessages;
            var runners = new List<NetworkRunner>();

            // Runtime only: the asset on disk is never saved.
            config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
            // Unrelated errors (voice, audio, missing optional services) must not hide the real assertions below.
            LogAssert.ignoreFailingMessages = true;

            try
            {
                var room = "HARNESS" + Guid.NewGuid().ToString("N");
                for (var i = 0; i < 2; i++)
                {
                    var runner = CreateRunner("HarnessPeer" + i);
                    runners.Add(runner);

                    var start = runner.StartGame(MakeArgs(runner, room));
                    var deadline = Time.realtimeSinceStartup + StepTimeout;
                    while (!start.IsCompleted && Time.realtimeSinceStartup < deadline)
                        yield return null;

                    if (!start.IsCompleted)
                        Assert.Ignore("No network: the session did not start in time (Photon unreachable?).");

                    if (start.IsFaulted || !start.Result.Ok)
                    {
                        var reason = start.IsFaulted ? ShutdownReason.Error : start.Result.ShutdownReason;
                        if (NetworkReasons.Contains(reason))
                            Assert.Ignore($"No network: Photon session failed to start ({reason}).");
                        Assert.Fail($"Session failed to start: {reason} {(start.IsFaulted ? start.Exception?.GetBaseException().Message : start.Result.ErrorMessage)}");
                    }
                }

                var peerA = runners[0];
                var peerB = runners[1];

                // Each peer has its own player and sees the world objects.
                yield return WaitUntil(() => FindLocal(peerA) != null && FindLocal(peerB) != null && Carryables(peerB).Count > 0,
                    StepTimeout, "both peers to have their player and the scene objects");

                // Interaction is only allowed while the match is Playing; ask for it and wait on both peers.
                var master = peerA.IsSharedModeMasterClient ? peerA : peerB;
                var other = master == peerA ? peerB : peerA;
                Assert.IsTrue(master.IsSharedModeMasterClient && !other.IsSharedModeMasterClient, "Exactly one peer must be the master client.");

                // The request is ignored until the authority sees both players, so it is repeated once a second.
                var nextAsk = 0f;
                yield return WaitUntil(() =>
                {
                    if (Time.realtimeSinceStartup >= nextAsk && !Game.MatchPlaying(other))
                    {
                        Game.StartMatch(other);
                        nextAsk = Time.realtimeSinceStartup + 1f;
                    }

                    return Game.MatchPlaying(peerA) && Game.MatchPlaying(peerB);
                }, StepTimeout, "the match to reach Playing on both peers");
                // Roles respawn the characters when the match starts: wait until each peer has its player again.
                yield return WaitUntil(() => FindLocal(peerA) != null && FindLocal(peerB) != null, StepTimeout, "players after the role respawn");
                yield return new WaitForSeconds(1f);

                // The non-master peer is the one that grabs: that is the path that goes through an RPC to the authority.
                var grabber = FindLocal(other);
                var item = PickNearestSingleHolderItem(other, grabber);
                Assert.IsNotNull(item, "No single-holder Carryable the non-master player can take.");

                var itemOnMaster = FindSame(master, item);
                Assert.IsNotNull(itemOnMaster, "The item must exist on the master peer too.");

                var grabberOnMaster = FindInteractorOf(master, other.LocalPlayer);
                Assert.IsNotNull(grabberOnMaster, "The grabber's character must exist on the master peer too.");

                // Same API the input layer calls (PlayerInteractor: Focused.CanInteract -> Focused.Interact).
                Assert.IsTrue(Game.CanInteract(item, grabber), "CanInteract must be true for the item picked.");
                Game.Interact(item, grabber);

                yield return WaitUntil(() => Game.IsHeldBy(item, grabber) && Game.IsHeldBy(itemOnMaster, grabberOnMaster),
                    GrabTimeout, "the item to be held by the grabber on both peers", failOnTimeout: false);
                Assert.IsTrue(Game.IsHeldBy(item, grabber), "Grabber's own peer does not see the item held.");
                Assert.IsTrue(Game.IsHeldBy(itemOnMaster, grabberOnMaster), "Master peer does not see the item held by the grabber.");

                // Release the same way the input does.
                Game.RequestRelease(item, grabber);
                yield return new WaitForSeconds(DropSettleSeconds);

                Assert.IsFalse(Game.IsHeld(item), "Item still held on the grabber's peer after release.");
                Assert.IsFalse(Game.IsHeld(itemOnMaster), "Item still held on the master peer after release.");

                var heightGrabber = HeightAboveGround(other, item);
                var heightMaster = HeightAboveGround(master, itemOnMaster);
                Assert.That(heightGrabber, Is.LessThan(MaxHeightAboveGround), "Item floats on the grabber's peer.");
                Assert.That(heightMaster, Is.LessThan(MaxHeightAboveGround), "Item floats on the master peer.");

                var difference = Vector3.Distance(item.transform.position, itemOnMaster.transform.position);
                Assert.That(difference, Is.LessThan(MaxPeerDifference), "Item position differs between peers.");
            }
            finally
            {
                // Always leave the process clean, whatever happened above.
                foreach (var runner in runners)
                {
                    if (runner == null)
                        continue;
                    var shutdown = runner.Shutdown();
                    var deadline = Time.realtimeSinceStartup + 10f;
                    while (!shutdown.IsCompleted && Time.realtimeSinceStartup < deadline)
                        yield return null;
                }

                config.PeerMode = previousPeerMode;
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        // ------------------------------------------------------------------ session setup

        private static NetworkRunner CreateRunner(string name)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
            // Scenes are identified by path, like the game does (see PathSceneManager in the game).
            go.AddComponent(Game.Type("BetterTogether.Networking.PathSceneManager"));
            return runner;
        }

        private static StartGameArgs MakeArgs(NetworkRunner runner, string room)
        {
            var scenes = new NetworkSceneInfo();
            scenes.AddSceneRef(SceneRef.FromPath(ScenePath), LoadSceneMode.Additive);
            return new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = room,
                PlayerCount = 8,
                Scene = scenes,
                SceneManager = (INetworkSceneManager)runner.GetComponent(Game.Type("BetterTogether.Networking.PathSceneManager")),
            };
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what, bool failOnTimeout = true)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    if (failOnTimeout)
                        Assert.Fail($"Timed out after {seconds:0}s waiting for {what}.");
                    yield break;
                }

                yield return null;
            }
        }

        // ------------------------------------------------------------------ finding each peer's own copy of an object

        // The runner that owns a behaviour is the way to tell the peers' copies apart: statics like
        // PlayerInteractor.Local are shared by every peer in the process, so they are never used here.
        private static List<NetworkBehaviour> OfRunner(NetworkRunner runner, string typeName)
        {
            var result = new List<NetworkBehaviour>();
            foreach (var found in UnityEngine.Object.FindObjectsByType(Game.Type(typeName), FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var behaviour = (NetworkBehaviour)found;
                if (behaviour.Runner == runner && behaviour.Object != null && behaviour.Object.IsValid)
                    result.Add(behaviour);
            }

            return result;
        }

        private static List<NetworkBehaviour> Carryables(NetworkRunner runner) => OfRunner(runner, Game.CarryableType);

        private static NetworkBehaviour FindLocal(NetworkRunner runner) => FindInteractorOf(runner, runner.LocalPlayer);

        private static NetworkBehaviour FindInteractorOf(NetworkRunner runner, PlayerRef player)
        {
            foreach (var interactor in OfRunner(runner, Game.InteractorType))
                if (interactor.Object.StateAuthority == player)
                    return interactor;
            return null;
        }

        private static NetworkBehaviour FindSame(NetworkRunner runner, NetworkBehaviour item) =>
            runner.TryFindObject(item.Object.Id, out var obj) ? obj.GetComponent(Game.CarryableType) as NetworkBehaviour : null;

        // Nearest loose, one-holder item that the game itself says the player can take.
        private static NetworkBehaviour PickNearestSingleHolderItem(NetworkRunner runner, NetworkBehaviour grabber)
        {
            NetworkBehaviour best = null;
            var bestDistance = float.MaxValue;
            foreach (var item in Carryables(runner))
            {
                if (Game.GnomesRequired(item) != 1 || Game.IsPinned(item) || !Game.CanInteract(item, grabber))
                    continue;

                var distance = (item.transform.position - grabber.transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
            }

            return best;
        }

        // Metres between the lowest collider point of the item and the Environment surface under it.
        private static float HeightAboveGround(NetworkRunner runner, NetworkBehaviour item)
        {
            var bottom = float.MaxValue;
            var center = item.transform.position;
            var found = false;
            foreach (var collider in item.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || !collider.enabled)
                    continue;
                bottom = Mathf.Min(bottom, collider.bounds.min.y);
                center = collider.bounds.center;
                found = true;
            }

            if (!found)
                bottom = item.transform.position.y;

            var mask = LayerMask.GetMask("Environment");
            var origin = new Vector3(center.x, bottom + 0.1f, center.z);
            var scene = runner.GetPhysicsScene();
            var hit = default(RaycastHit);
            var ok = scene.IsValid()
                ? scene.Raycast(origin, Vector3.down, out hit, 50f, mask, QueryTriggerInteraction.Ignore)
                : Physics.Raycast(origin, Vector3.down, out hit, 50f, mask, QueryTriggerInteraction.Ignore);
            Assert.IsTrue(ok, $"No Environment ground found under {item.name} on {runner.name}.");
            return bottom - hit.point.y;
        }

        // ------------------------------------------------------------------ game API through reflection

        private static class Game
        {
            public const string CarryableType = "BetterTogether.Interaction.Carryable";
            public const string InteractorType = "BetterTogether.Interaction.PlayerInteractor";

            private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();
            private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            public static Type Type(string fullName)
            {
                if (Types.TryGetValue(fullName, out var cached))
                    return cached;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType(fullName);
                    if (type == null)
                        continue;
                    Types[fullName] = type;
                    return type;
                }

                throw new InvalidOperationException($"Game type {fullName} not found; is FUSION_WEAVER defined and did the game compile?");
            }

            public static bool CanInteract(NetworkBehaviour item, NetworkBehaviour who) =>
                (bool)Method(item, "CanInteract", Type(InteractorType)).Invoke(item, new object[] { who });

            public static void Interact(NetworkBehaviour item, NetworkBehaviour who) =>
                Method(item, "Interact", Type(InteractorType)).Invoke(item, new object[] { who });

            public static void RequestRelease(NetworkBehaviour item, NetworkBehaviour who) =>
                Method(item, "RequestRelease", Type(InteractorType)).Invoke(item, new object[] { who });

            public static bool IsHeld(NetworkBehaviour item) => (bool)Prop(item, "IsHeld");

            public static bool IsPinned(NetworkBehaviour item) => (bool)Prop(item, "IsPinned");

            // The replicated holder, read the way the game does (Carryable.FindHeldBy via PlayerInteractor.Carried).
            public static bool IsHeldBy(NetworkBehaviour item, NetworkBehaviour who) => ReferenceEquals(Prop(who, "Carried"), item);

            public static int GnomesRequired(NetworkBehaviour item) =>
                (int)item.GetType().GetField("_gnomesRequired", Any).GetValue(item);

            // Asks for the match start through the public API; true once asked.
            public static bool StartMatch(NetworkRunner runner)
            {
                var manager = Manager(runner);
                if (manager == null)
                    return false;
                Method(manager, "StartMatch").Invoke(manager, null);
                return true;
            }

            public static bool MatchPlaying(NetworkRunner runner)
            {
                var manager = Manager(runner);
                return manager != null && string.Equals(Prop(manager, "Phase").ToString(), "Playing", StringComparison.Ordinal);
            }

            private static NetworkBehaviour Manager(NetworkRunner runner)
            {
                var found = OfRunner(runner, "BetterTogether.Match.MatchManager");
                return found.Count > 0 ? found[0] : null;
            }

            private static MethodInfo Method(object target, string name, params Type[] args) =>
                target.GetType().GetMethod(name, Any, null, args, null)
                ?? throw new MissingMethodException(target.GetType().Name, name);

            private static object Prop(object target, string name) =>
                (target.GetType().GetProperty(name, Any) ?? throw new MissingMemberException(target.GetType().Name, name)).GetValue(target);
        }
    }
}
