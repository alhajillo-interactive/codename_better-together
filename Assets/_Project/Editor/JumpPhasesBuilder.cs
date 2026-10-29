// Editor-only builder for the three-phase jump (C5.5). Run it from the menu GnoMore/Construir fases de salto.
// It edits Campista.controller and GnomeController.controller (never by hand): adds the parameter VerticalSpeed (Float)
// (and removes the obsolete Land trigger), turns the old single "Jump" state into JumpUp and adds Fall and Land states, all with the same jump clip:
//   JumpUp: the clip from 0 at normal speed (left as soon as the vertical speed turns negative).
//   Fall:   speed 0 with cycleOffset = PeakFraction, i.e. the pose of the jump peak held for as long as the fall lasts.
//   Land:   the last LandSeconds of the clip (cycleOffset), then back to Locomotion by exit time.
// Transitions: Locomotion -> JumpUp (IsJump, VerticalSpeed > 0.1), Locomotion -> Fall (IsJump, VerticalSpeed < 0.1: walking off an
// edge), JumpUp -> Fall (VerticalSpeed < 0), Fall -> JumpUp (VerticalSpeed > 0.5, recovers a start picked before the speed was known),
// JumpUp/Fall -> Land (IsJump false). The Any State transitions (kick, stun, statue)
// are untouched. It is idempotent: states and transitions it owns are rebuilt on every run. A controller without the jump clip
// is left unchanged and the reason is logged.
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BetterTogether.EditorTools
{
    public static class JumpPhasesBuilder
    {
        private const string LogPrefix = "[JumpPhasesBuilder]";
        private const string CamperPath = "Assets/_Project/Models/CampistaAnimations/Campista.controller";
        private const string GnomePath = "Assets/_Project/Models/GnomoAnimation/GnomeController.controller";
        private const string LocomotionName = "Locomotion";
        private const string JumpUpName = "JumpUp";
        private const string LegacyJumpName = "Jump";
        private const string FallName = "Fall";
        private const string LandName = "Land";
        private const string JumpParam = "IsJump";
        private const string SpeedParam = "VerticalSpeed";
        private const string ObsoleteLandTrigger = "Land";

        /// <summary>Normalized time of the clip where the jump peaks (the Fall pose).</summary>
        private const float PeakFraction = 0.45f;
        /// <summary>Seconds of the end of the clip played as the landing.</summary>
        private const float LandSeconds = 0.2f;
        private const float BlendSeconds = 0.08f;

        [MenuItem("GnoMore/Construir fases de salto")]
        public static void Build()
        {
            var changed = BuildController(CamperPath);
            changed |= BuildController(GnomePath);
            if (changed)
                AssetDatabase.SaveAssets();
            Debug.Log($"{LogPrefix} Done.");
        }

        private static bool BuildController(string path)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                Debug.LogError($"{LogPrefix} Controller not found: {path}");
                return false;
            }

            var machine = controller.layers[0].stateMachine;
            var locomotion = FindState(machine, LocomotionName);
            var jumpUp = FindState(machine, JumpUpName) ?? FindState(machine, LegacyJumpName);
            var clip = jumpUp != null ? jumpUp.motion as AnimationClip : null;
            if (locomotion == null || jumpUp == null || clip == null || clip.length <= 0.01f)
            {
                Debug.LogWarning($"{LogPrefix} {path}: no Locomotion/Jump state with a clip; left unchanged.");
                return false;
            }

            EnsureParameter(controller, SpeedParam, AnimatorControllerParameterType.Float);
            RemoveParameter(controller, ObsoleteLandTrigger);

            // Rebuild what this tool owns: Fall and Land (removing a state drops its transitions) and the jump transitions.
            RemoveState(machine, FallName);
            RemoveState(machine, LandName);
            foreach (var transition in locomotion.transitions.Where(t => t.destinationState == jumpUp).ToArray())
                locomotion.RemoveTransition(transition);
            // Only the transitions this tool creates: the ones leaving JumpUp towards Fall, Land or Locomotion.
            foreach (var transition in jumpUp.transitions.Where(t => t.destinationState == null || t.destinationState == locomotion).ToArray())
                jumpUp.RemoveTransition(transition);

            jumpUp.name = JumpUpName;
            jumpUp.speed = 1f;
            jumpUp.cycleOffset = 0f;

            var length = clip.length;
            var landStart = Mathf.Clamp01(1f - LandSeconds / length);
            var fall = machine.AddState(FallName, new Vector3(270f, 300f, 0f));
            fall.motion = clip;
            fall.speed = 0f;
            fall.cycleOffset = PeakFraction;
            fall.writeDefaultValues = true;

            var land = machine.AddState(LandName, new Vector3(520f, 300f, 0f));
            land.motion = clip;
            land.speed = 1f;
            land.cycleOffset = landStart;
            land.writeDefaultValues = true;

            var enterJump = Add(locomotion, jumpUp, 0.1f);
            enterJump.AddCondition(AnimatorConditionMode.If, 0f, JumpParam);
            enterJump.AddCondition(AnimatorConditionMode.Greater, 0.1f, SpeedParam);

            var enterFall = Add(locomotion, fall, 0.1f);
            enterFall.AddCondition(AnimatorConditionMode.If, 0f, JumpParam);
            enterFall.AddCondition(AnimatorConditionMode.Less, 0.1f, SpeedParam);

            Add(jumpUp, fall, 0.1f).AddCondition(AnimatorConditionMode.Less, 0f, SpeedParam);

            var recover = Add(fall, jumpUp, 0.05f);
            recover.AddCondition(AnimatorConditionMode.If, 0f, JumpParam);
            recover.AddCondition(AnimatorConditionMode.Greater, 0.5f, SpeedParam);

            foreach (var source in new[] { jumpUp, fall })
            {
                Add(source, land, 0.05f).AddCondition(AnimatorConditionMode.IfNot, 0f, JumpParam);
            }

            // Back to Locomotion near the end of the clip. Normalized time starts at 0 in the state whatever the cycle
            // offset, so the exit time is measured from landStart: the remaining fraction minus the blend.
            var remaining = 1f - landStart;
            var exitTime = Mathf.Max(remaining * 0.5f, remaining - BlendSeconds / length);
            var exit = land.AddTransition(locomotion);
            exit.hasExitTime = true;
            exit.exitTime = Mathf.Clamp(exitTime, 0.05f, 1f);
            exit.hasFixedDuration = true;
            exit.duration = BlendSeconds;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(jumpUp);
            EditorUtility.SetDirty(fall);
            EditorUtility.SetDirty(land);
            Debug.Log($"{LogPrefix} {path}: clip '{clip.name}' ({length:0.00}s), peak at {PeakFraction:0.00}, land from {landStart:0.00}.");
            return true;
        }

        private static AnimatorStateTransition Add(AnimatorState source, AnimatorState to, float duration)
        {
            var transition = source.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            return transition;
        }

        private static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            foreach (var child in machine.states)
            {
                if (child.state.name == name)
                    return child.state;
            }

            return null;
        }

        private static void RemoveState(AnimatorStateMachine machine, string name)
        {
            var state = FindState(machine, name);
            if (state != null)
                machine.RemoveState(state);
        }

        private static void RemoveParameter(AnimatorController controller, string name)
        {
            var parameters = controller.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name)
                {
                    controller.RemoveParameter(i);
                    return;
                }
            }
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (controller.parameters.Any(p => p.name == name && p.type == type))
                return;

            if (controller.parameters.Any(p => p.name == name))
            {
                Debug.LogWarning($"{LogPrefix} Parameter '{name}' exists with another type; remove it by hand.");
                return;
            }

            controller.AddParameter(name, type);
        }
    }
}
