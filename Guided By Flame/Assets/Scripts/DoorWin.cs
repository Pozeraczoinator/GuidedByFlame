using UnityEngine;

public class DoorWin : MonoBehaviour
{
    private bool playerIsNear = false;

    void Update()
    {
        if (playerIsNear && KeyPickup.hasKey)
        {
            Debug.Log("Wygrałeś!");
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerIsNear = true;
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerIsNear = false;
        }
    }
}