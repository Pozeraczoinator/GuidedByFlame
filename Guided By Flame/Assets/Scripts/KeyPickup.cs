using UnityEngine;

public class KeyPickup : MonoBehaviour
{
    public static bool hasKey = false;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            hasKey = true;
            Destroy(gameObject); // usuń klucz z ziemi
            Debug.Log("Klucz zebrany!");
        }
    }
}
