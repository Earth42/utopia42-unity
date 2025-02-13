using System.Linq;
using UnityEngine;

namespace Source.Canvas
{
    public class StateAware : MonoBehaviour
    {
        [SerializeField] private GameManager.State[] states;

        private void Start()
        {
            GameManager.INSTANCE.stateChange.AddListener(UpdateVisibility);
            UpdateVisibility(GameManager.INSTANCE.GetState());
        }

        private void UpdateVisibility(GameManager.State s)
        {
            gameObject.SetActive(states.Any(state => s == state));
        }
    }
}