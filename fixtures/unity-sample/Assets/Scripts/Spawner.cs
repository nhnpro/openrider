using UnityEngine;

namespace Game
{
    /// <summary>Spawns <see cref="count"/> copies of a player prefab at start.</summary>
    public class Spawner : MonoBehaviour
    {
        [SerializeField] private Player prefab;
        [SerializeField] private int count = 3;
        [SerializeField] private float spacing = 1.5f;

        private void Start()
        {
            for (var i = 0; i < count; i++)
            {
                Instantiate(prefab, transform.position + Vector3.right * (i * spacing), Quaternion.identity);
            }
        }
    }
}
