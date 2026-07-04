using UnityEngine;

public class MinimapCamerNoRotation : MonoBehaviour
{

    public GameObject Player;

    // Update is called once per frame
    private void LateUpdate()
    {
        transform.position = new Vector3(Player.transform.position.x, transform.position.y, Player.transform.position.z);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
}
