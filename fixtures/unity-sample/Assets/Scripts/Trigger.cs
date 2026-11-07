using UnityEngine;
using UnityEngine.Events;

namespace Game
{
    /// <summary>Raises <see cref="onEnter"/> when something enters its collider.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Trigger : MonoBehaviour
    {
        [SerializeField] private UnityEvent onEnter = new UnityEvent();

        public UnityEvent OnEnter => onEnter;

        private void OnTriggerEnter(Collider other) => onEnter.Invoke();
    }
}
