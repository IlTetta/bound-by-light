using UnityEngine;

public class SimpleRotate : MonoBehaviour
{
    [SerializeField] private float speed = 90f;
    [SerializeField] private Vector3 axis = Vector3.up;

    private void Update() => transform.Rotate(axis, speed * Time.deltaTime, Space.Self);
}
