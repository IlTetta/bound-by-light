using UnityEngine;

namespace BoundByLight.Core
{
    public class WindowSettings : MonoBehaviour
    {
        [SerializeField] private int width = 1280;
        [SerializeField] private int height = 720;

        void Awake()
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }
    }
}
