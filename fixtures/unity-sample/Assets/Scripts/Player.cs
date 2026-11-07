using UnityEngine;

namespace Game
{
    /// <summary>The player: moves forward and counts the coins it touches.</summary>
    public class Player : MonoBehaviour
    {
        [SerializeField] private float speed = 2f;
        [SerializeField] private int coins;

        public float Speed => speed;
        public int Coins => coins;

        private void Update()
        {
            // Set a breakpoint here (QA: debugger) while in Play mode
            transform.Translate(Vector3.forward * (speed * Time.deltaTime));
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Finish"))
            {
                AddCoin();
            }
        }

        public void AddCoin() => coins++;

        /// <summary>Bound to a <see cref="Trigger"/>'s UnityEvent in Main.unity.</summary>
        public void OnTriggerZoneEntered()
        {
            // TODO: play a sound
            AddCoin();
        }

        /// <summary>Called by the "Footstep" animation event of Walk.anim.</summary>
        public void OnFootstep()
        {
            var unused = 0; // QA: intentional warning (CS0219, assigned but never used)
        }
    }
}
