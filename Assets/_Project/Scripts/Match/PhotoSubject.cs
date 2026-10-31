using UnityEngine;

namespace BetterTogether.Match
{
    /// <summary>
    /// A place the camper can be asked to photograph. Scene object without networking: its position is the point that has to be
    /// in the camera's view, in the open so scenery does not hide it. Listed by <see cref="MissionLayout"/>.
    /// </summary>
    public class PhotoSubject : MonoBehaviour
    {
        [Tooltip("Shown to the player, in Spanish, e.g. 'Foto de la Poza'.")]
        [SerializeField] private string _label = "Foto";
        [Tooltip("Farthest the camper can be (m). Places are bigger than a gnome, so this exceeds the camera's freezing range.")]
        [SerializeField, Min(1f)] private float _maxDistance = 18f;

        public string Label => _label;

        public float MaxDistance => _maxDistance;

        public Vector3 Point => transform.position;
    }
}
