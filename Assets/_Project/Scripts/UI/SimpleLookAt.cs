using UnityEngine;
using Unity.Netcode;


public class SimpleLookAt : MonoBehaviour
{
    public Transform canvas;
    Camera mainCam;
    public Quaternion rotation;

    void Start()
    {
        mainCam = Camera.main;
    }


    void Update()
    {
        canvas.LookAt(mainCam.transform.forward);
        transform.rotation = rotation;
    }

}
